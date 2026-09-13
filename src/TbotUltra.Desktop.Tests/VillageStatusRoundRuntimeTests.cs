using TbotUltra.Desktop.Services.Orchestration;
using Xunit;

namespace TbotUltra.Desktop.Tests;

public sealed class VillageStatusRoundRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetNextRoundUtc_LoadsEachAccountScheduleWhenItBecomesActive()
    {
        var state = new InMemoryStatePort();
        state.Saved["a"] = Now.AddMinutes(10);
        state.Saved["b"] = Now.AddMinutes(20);
        var runtime = CreateRuntime(state);

        Assert.Equal(Now.AddMinutes(10), runtime.GetNextRoundUtc("a"));
        Assert.Equal(Now.AddMinutes(10), runtime.GetNextRoundUtc("A"));
        Assert.Equal(Now.AddMinutes(20), runtime.GetNextRoundUtc("b"));
        Assert.Equal(2, state.LoadCount);
    }

    [Fact]
    public void ScheduleNext_UsesControlledTimeAndRandomnessAndPersistsTheDeadline()
    {
        var state = new InMemoryStatePort();
        var runtime = CreateRuntime(state, nextRandom: (min, max) => max - 1);

        var result = runtime.ScheduleNext("a", "A", minMinutes: 10, maxMinutes: 30);

        Assert.Equal(Now.AddMinutes(30), result.NextRoundUtc);
        Assert.True(result.WasPersisted);
        Assert.False(result.AccountChanged);
        Assert.Equal(result.NextRoundUtc, state.Saved["A"]);
    }

    [Fact]
    public void ScheduleNext_DoesNotPersistAfterTheAccountChanges()
    {
        var state = new InMemoryStatePort();
        var runtime = CreateRuntime(state);

        var result = runtime.ScheduleNext("a", "b", 10, 30);

        Assert.True(result.AccountChanged);
        Assert.Null(result.NextRoundUtc);
        Assert.Empty(state.Saved);
    }

    [Fact]
    public void ForceRequests_CoalesceUntilConsumed()
    {
        var runtime = CreateRuntime(new InMemoryStatePort());

        runtime.RequestForce();
        runtime.RequestForce();

        Assert.True(runtime.ConsumeForceRequest());
        Assert.False(runtime.ConsumeForceRequest());
    }

    [Fact]
    public void ForceOnWakeRequest_RemainsSeparateUntilWakeConsumesIt()
    {
        var runtime = CreateRuntime(new InMemoryStatePort());

        runtime.SetForceOnWakeRequest(true);

        Assert.False(runtime.ConsumeForceRequest());
        Assert.True(runtime.ConsumeForceOnWakeRequest());
        Assert.False(runtime.ConsumeForceOnWakeRequest());
    }

    [Fact]
    public void ManualRunGate_AllowsOnlyOneOwnerUntilReleased()
    {
        var runtime = CreateRuntime(new InMemoryStatePort());

        Assert.True(runtime.TryBeginManualRun());
        Assert.False(runtime.TryBeginManualRun());
        runtime.EndManualRun();
        Assert.True(runtime.TryBeginManualRun());
    }

    [Fact]
    public void Reset_ClearsTheRememberedDeadline()
    {
        var state = new InMemoryStatePort();
        state.Saved["a"] = Now.AddMinutes(10);
        var runtime = CreateRuntime(state);
        Assert.NotEqual(DateTimeOffset.MinValue, runtime.GetNextRoundUtc("a"));

        var cleared = runtime.Reset("a");

        Assert.True(cleared);
        Assert.Equal(DateTimeOffset.MinValue, runtime.GetNextRoundUtc("a"));
    }

    private static VillageStatusRoundRuntime CreateRuntime(
        InMemoryStatePort state,
        Func<int, int, int>? nextRandom = null) => new(
        state,
        new FixedTimeProvider(Now),
        nextRandom ?? ((min, _) => min));

    private sealed class InMemoryStatePort : IVillageStatusRoundStatePort
    {
        internal Dictionary<string, DateTimeOffset> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal int LoadCount { get; private set; }

        public DateTimeOffset? Load(string? accountName, DateTimeOffset nowUtc)
        {
            LoadCount++;
            return accountName is not null && Saved.TryGetValue(accountName, out var deadline) && deadline > nowUtc
                ? deadline
                : null;
        }

        public bool Save(string? accountName, DateTimeOffset nextRoundUtc)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                return false;
            }

            Saved[accountName] = nextRoundUtc;
            return true;
        }

        public bool Clear(string? accountName) =>
            !string.IsNullOrWhiteSpace(accountName) && Saved.Remove(accountName);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
