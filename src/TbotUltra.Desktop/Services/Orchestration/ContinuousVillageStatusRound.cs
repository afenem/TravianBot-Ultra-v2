using TbotUltra.Core.Configuration;

namespace TbotUltra.Desktop.Services.Orchestration;

internal interface IContinuousVillageStatusRoundPort : IVillageStatusRoundPort
{
    DateTimeOffset GetNextRoundUtc();
    string? ActiveAccountName { get; }
    ValueTask<bool> EnsureVillageMembershipVerifiedAsync(
        BotOptions options,
        CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<VillageStatusRoundVillage>> LoadVillagesAsync(BotOptions options);
    IDisposable BeginRoundActivity(int villageCount);
    VillageStatusRoundScheduleResult ScheduleNext(
        string? expectedAccountName,
        int minMinutes,
        int maxMinutes);
    void Log(string message);
}

internal sealed class ContinuousVillageStatusRound(
    VillageStatusRoundCoordinator coordinator,
    IContinuousVillageStatusRoundPort port,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    internal async ValueTask RunIfDueAsync(
        BotOptions options,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if ((!options.VillageStatusSweepEnabled && !force)
            || (!force && _timeProvider.GetUtcNow() < port.GetNextRoundUtc()))
        {
            return;
        }

        if (!await port.EnsureVillageMembershipVerifiedAsync(options, cancellationToken))
        {
            port.Log("[village-scan] round deferred until village ownership can be verified.");
            return;
        }

        var accountName = port.ActiveAccountName;
        var villages = await port.LoadVillagesAsync(options);
        if (villages.Count == 0)
        {
            return;
        }

        using var activity = port.BeginRoundActivity(villages.Count);
        var result = await coordinator.RunAsync(villages, port, cancellationToken);
        if (!result.Completed)
        {
            return;
        }

        var min = Math.Min(
            options.VillageStatusSweepRoundMinMinutes,
            options.VillageStatusSweepRoundMaxMinutes);
        var max = Math.Max(
            options.VillageStatusSweepRoundMinMinutes,
            options.VillageStatusSweepRoundMaxMinutes);
        var schedule = port.ScheduleNext(accountName, min, max);
        if (schedule.AccountChanged)
        {
            return;
        }
        if (!schedule.WasPersisted)
        {
            port.Log(
                "[village-scan] could not persist the next-scan deadline; "
                + "it may run again after restart.");
        }
        if (schedule.NextRoundUtc is { } nextRoundUtc)
        {
            port.Log($"[village-scan] round complete; next round after {nextRoundUtc:HH:mm}.");
        }
    }
}
