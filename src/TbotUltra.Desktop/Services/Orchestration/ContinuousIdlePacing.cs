using TbotUltra.Core.Configuration;

namespace TbotUltra.Desktop.Services.Orchestration;

internal interface IContinuousIdlePacingPort
{
    bool SessionAvailable { get; }
    bool StopRequested { get; }
    bool ImmediateWorkRequested { get; }
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    IDisposable BeginBrowseActivity();
    ValueTask NavigateAsync(BotOptions options, string path, CancellationToken cancellationToken);
    void Log(string message);
}

internal sealed class ContinuousIdlePacing(
    AutomationIdlePacing pacing,
    IContinuousIdlePacingPort port,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan MaximumBreakSlice = TimeSpan.FromSeconds(1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    internal async ValueTask MaybeTakeBreakAsync(
        BotOptions options,
        CancellationToken cancellationToken)
    {
        var plan = pacing.PlanBreak(options, port.SessionAvailable);
        if (!plan.ShouldTakeBreak)
        {
            return;
        }

        port.Log($"[pacing] idle break: stepping away for {plan.DurationSeconds}s.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(plan.DurationSeconds);
        while (_timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port.StopRequested)
            {
                port.Log("[pacing] idle break canceled by stop.");
                return;
            }
            if (port.ImmediateWorkRequested)
            {
                port.Log("[pacing] idle break ended early: queue state or settings changed.");
                pacing.CompleteBreak(options);
                return;
            }

            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            await port.DelayAsync(
                remaining < MaximumBreakSlice ? remaining : MaximumBreakSlice,
                cancellationToken);
        }

        port.Log("[pacing] idle break over; resuming.");
        pacing.CompleteBreak(options);
    }

    internal async ValueTask MaybeBrowseAsync(
        BotOptions options,
        CancellationToken cancellationToken)
    {
        var plan = pacing.PlanBrowse(options, port.SessionAvailable);
        if (plan.NoPageSelected)
        {
            port.Log("[pacing:verbose] idle browse skipped: no pages selected.");
            return;
        }
        if (!plan.ShouldBrowse)
        {
            return;
        }

        var page = plan.Page!;
        port.Log($"[pacing] idle browse: viewing {page}.");
        using var activity = port.BeginBrowseActivity();
        try
        {
            if (AutomationIdlePacing.RequiresStatisticsLandingPage(page))
            {
                port.Log(
                    "[pacing:verbose] idle browse: opening the statistics overview before "
                    + "the selected statistics page.");
                await port.NavigateAsync(options, "/statistics", cancellationToken);
            }
            await port.NavigateAsync(options, page, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            port.Log($"[pacing:verbose] idle browse skipped after page failure ({ex.Message}).");
        }

        pacing.CompleteBrowse(options);
    }
}
