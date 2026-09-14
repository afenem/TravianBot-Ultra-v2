using TbotUltra.Core.Configuration;
using TbotUltra.Desktop.Services.Orchestration;
using Xunit;

namespace TbotUltra.Desktop.Tests;

public sealed class ContinuousIdlePacingTests
{
    [Fact]
    public async Task StatisticsBrowse_OpensLandingPageBeforeSelectedPage()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var pacingState = new AutomationIdlePacing(
            time,
            nextDouble: () => 0,
            nextInt: (_, _) => 0);
        var port = new InMemoryPort();
        var pacing = new ContinuousIdlePacing(pacingState, port, time);
        var options = new BotOptions
        {
            ActionPacingIdleBrowseEnabled = true,
            ActionPacingIdleBrowseIntervalMinMinutes = 10,
            ActionPacingIdleBrowseIntervalMaxMinutes = 10,
            ActionPacingIdleBrowsePageMap = false,
            ActionPacingIdleBrowsePageStatistics = false,
            ActionPacingIdleBrowsePageStatisticsHero = true,
            ActionPacingIdleBrowsePageStatisticsTop10 = false,
            ActionPacingIdleBrowsePageStatisticsDefenders = false,
            ActionPacingIdleBrowsePageStatisticsAttackers = false,
            ActionPacingIdleBrowsePageReports = false,
            ActionPacingIdleBrowsePageMessages = false,
        };
        await pacing.MaybeBrowseAsync(options, default);
        time.Advance(TimeSpan.FromMinutes(10));

        await pacing.MaybeBrowseAsync(options, default);

        Assert.Equal(
            ["activity", "navigate:/statistics", "navigate:/statistics/hero", "dispose"],
            port.Trace);
    }

    private sealed class InMemoryPort : IContinuousIdlePacingPort
    {
        public List<string> Trace { get; } = [];
        public bool SessionAvailable => true;
        public bool StopRequested => false;
        public bool ImmediateWorkRequested => false;
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public IDisposable BeginBrowseActivity()
        {
            Trace.Add("activity");
            return new CallbackDisposable(() => Trace.Add("dispose"));
        }
        public ValueTask NavigateAsync(
            BotOptions options,
            string path,
            CancellationToken cancellationToken)
        {
            Trace.Add($"navigate:{path}");
            return ValueTask.CompletedTask;
        }
        public void Log(string message) { }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}
