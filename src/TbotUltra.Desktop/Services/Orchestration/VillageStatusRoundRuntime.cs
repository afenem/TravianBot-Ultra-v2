namespace TbotUltra.Desktop.Services.Orchestration;

internal interface IVillageStatusRoundStatePort
{
    DateTimeOffset? Load(string? accountName, DateTimeOffset nowUtc);

    bool Save(string? accountName, DateTimeOffset nextRoundUtc);

    bool Clear(string? accountName);
}

internal readonly record struct VillageStatusRoundScheduleResult(
    DateTimeOffset? NextRoundUtc,
    bool WasPersisted,
    bool AccountChanged);

internal sealed class VillageStatusRoundRuntime(
    IVillageStatusRoundStatePort state,
    TimeProvider? timeProvider = null,
    Func<int, int, int>? nextRandom = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Func<int, int, int> _nextRandom = nextRandom ?? Random.Shared.Next;
    private string? _scheduleAccountName;
    private DateTimeOffset _nextRoundUtc = DateTimeOffset.MinValue;
    private int _forceRequested;
    private int _forceOnWakeRequested;
    private int _manualRunInProgress;

    internal DateTimeOffset GetNextRoundUtc(string? accountName)
    {
        lock (_sync)
        {
            if (string.Equals(_scheduleAccountName, accountName, StringComparison.OrdinalIgnoreCase))
            {
                return _nextRoundUtc;
            }

            _scheduleAccountName = accountName;
            _nextRoundUtc = state.Load(accountName, _timeProvider.GetUtcNow()) ?? DateTimeOffset.MinValue;
            return _nextRoundUtc;
        }
    }

    internal bool Reset(string? accountName)
    {
        lock (_sync)
        {
            _scheduleAccountName = accountName;
            _nextRoundUtc = DateTimeOffset.MinValue;
            return state.Clear(accountName);
        }
    }

    internal VillageStatusRoundScheduleResult ScheduleNext(
        string? expectedAccountName,
        string? currentAccountName,
        int minMinutes,
        int maxMinutes)
    {
        lock (_sync)
        {
            if (!string.Equals(expectedAccountName, currentAccountName, StringComparison.OrdinalIgnoreCase))
            {
                return new VillageStatusRoundScheduleResult(null, false, AccountChanged: true);
            }

            var delayMinutes = _nextRandom(minMinutes, maxMinutes + 1);
            var nextRoundUtc = _timeProvider.GetUtcNow().AddMinutes(delayMinutes);
            _scheduleAccountName = currentAccountName;
            _nextRoundUtc = nextRoundUtc;
            return new VillageStatusRoundScheduleResult(
                nextRoundUtc,
                state.Save(currentAccountName, nextRoundUtc),
                AccountChanged: false);
        }
    }

    internal void RequestForce() => Interlocked.Exchange(ref _forceRequested, 1);

    internal bool ConsumeForceRequest() => Interlocked.Exchange(ref _forceRequested, 0) == 1;

    internal void SetForceOnWakeRequest(bool requested) =>
        Interlocked.Exchange(ref _forceOnWakeRequested, requested ? 1 : 0);

    internal bool ConsumeForceOnWakeRequest() =>
        Interlocked.Exchange(ref _forceOnWakeRequested, 0) == 1;

    internal bool TryBeginManualRun() =>
        Interlocked.CompareExchange(ref _manualRunInProgress, 1, 0) == 0;

    internal void EndManualRun() => Interlocked.Exchange(ref _manualRunInProgress, 0);
}
