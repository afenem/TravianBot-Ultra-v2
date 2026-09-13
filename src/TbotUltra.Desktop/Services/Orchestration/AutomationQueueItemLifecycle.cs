using System.Diagnostics;
using TbotUltra.Core.Configuration;
using TbotUltra.Worker.Domain;

namespace TbotUltra.Desktop.Services.Orchestration;

internal readonly record struct QueueItemGuardResult(
    bool Handled,
    bool FreshBuildingsRefreshDone)
{
    internal static QueueItemGuardResult NotHandled { get; } = new(false, false);
}

internal interface IAutomationQueueItemLifecyclePort
{
    bool IsAllowedByAutomationSettings(QueueItem item);
    IDisposable BeginExecutionScope(QueueItem item);
    BotOptions LoadCurrentOptions();
    void MarkRunning(QueueItem item);
    ValueTask<QueueItemGuardResult> RunPreExecutionGuardsAsync(
        QueueItem item,
        BotOptions options,
        string logPrefix,
        Stopwatch timer,
        CancellationToken cancellationToken);
    BotOptions ApplyQueueItemOptions(BotOptions options, QueueItem item);
    CancellationToken BeginQueueItemOperation(QueueItem item, CancellationToken cancellationToken);
    ValueTask<BotTaskExecutionResult> ExecuteWorkerAsync(
        BotOptions options,
        QueueItem item,
        CancellationToken cancellationToken);
    ValueTask<bool> TryRecoverMissingBuildingUpgradeAsync(
        QueueItem item,
        BotOptions options,
        BotTaskExecutionResult executionResult,
        string logPrefix,
        Stopwatch timer,
        CancellationToken cancellationToken);
    ValueTask<bool> HandleSucceededAsync(
        QueueItem item,
        BotOptions options,
        BotTaskExecutionResult executionResult,
        CancellationToken cancellationToken);
    bool IsLoadBuildingsSnapshot(QueueItem item);
    ValueTask LoadBuildingsSnapshotAsync(CancellationToken cancellationToken);
    void MarkNetworkConnectionHealthy();
    void PublishLastScan();
    bool IsDemolition(QueueItem item);
    bool WasDemolitionStopped(Guid itemId);
    void MarkDeferred(Guid itemId);
    ValueTask<bool> HandleFailureAsync(
        QueueItem item,
        Exception exception,
        string logPrefix,
        Stopwatch timer,
        AutomationRunMode mode);
    ValueTask FinalizeExecutionAsync(
        QueueItem item,
        AutomationRunMode mode,
        bool freshBuildingsRefreshDone,
        CancellationToken cancellationToken);
    void Log(string message);
}

internal sealed class AutomationQueueItemLifecycle(IAutomationQueueItemLifecyclePort port)
{
    internal async ValueTask<bool> ExecuteAsync(
        QueueItem item,
        BotOptions options,
        string logPrefix,
        AutomationRunMode mode,
        CancellationToken cancellationToken)
    {
        if (!port.IsAllowedByAutomationSettings(item))
        {
            port.Log(
                $"{logPrefix} SKIP task={item.TaskName}, id={item.Id} "
                + "because automation is disabled for its village.");
            return true;
        }

        using var executionScope = port.BeginExecutionScope(item);
        var timer = Stopwatch.StartNew();
        options = RefreshHeroOptions(item, options);
        port.MarkRunning(item);
        var freshBuildingsRefreshDone = false;

        try
        {
            if (!port.IsAllowedByAutomationSettings(item))
            {
                port.MarkDeferred(item.Id);
                port.Log(
                    $"{logPrefix} SKIP task={item.TaskName}, id={item.Id} "
                    + "because automation was disabled for its village before execution.");
                return true;
            }

            var guard = await port.RunPreExecutionGuardsAsync(
                item,
                options,
                logPrefix,
                timer,
                cancellationToken);
            if (guard.Handled)
            {
                freshBuildingsRefreshDone = guard.FreshBuildingsRefreshDone;
                return true;
            }

            var effectiveOptions = port.ApplyQueueItemOptions(options, item);
            var executionToken = port.BeginQueueItemOperation(item, cancellationToken);
            var executionResult = await port.ExecuteWorkerAsync(effectiveOptions, item, executionToken);
            if (await port.TryRecoverMissingBuildingUpgradeAsync(
                    item,
                    options,
                    executionResult,
                    logPrefix,
                    timer,
                    cancellationToken))
            {
                return true;
            }

            freshBuildingsRefreshDone = await port.HandleSucceededAsync(
                item,
                options,
                executionResult,
                cancellationToken);
            if (port.IsLoadBuildingsSnapshot(item))
            {
                await port.LoadBuildingsSnapshotAsync(cancellationToken);
            }

            port.Log(FormatSuccessLog(logPrefix, timer, item, mode));
            port.MarkNetworkConnectionHealthy();
            if (mode == AutomationRunMode.ContinuousLoop)
            {
                port.PublishLastScan();
            }

            return true;
        }
        catch (OperationCanceledException) when (
            port.IsDemolition(item) && port.WasDemolitionStopped(item.Id))
        {
            port.Log(
                $"{logPrefix} STOPPED {timer.Elapsed.TotalSeconds:F1}s task={item.TaskName} | "
                + "canceled before the Official demolish click");
            return false;
        }
        catch (OperationCanceledException)
        {
            port.MarkDeferred(item.Id);
            port.Log(
                $"{logPrefix} PAUSED {timer.Elapsed.TotalSeconds:F1}s task={item.TaskName} | "
                + "queued item kept for retry");
            return false;
        }
        catch (Exception ex)
        {
            return await port.HandleFailureAsync(item, ex, logPrefix, timer, mode);
        }
        finally
        {
            await port.FinalizeExecutionAsync(
                item,
                mode,
                freshBuildingsRefreshDone,
                cancellationToken);
        }
    }

    private BotOptions RefreshHeroOptions(QueueItem item, BotOptions options)
    {
        if (!string.Equals(item.TaskName, "hero_manage", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                item.TaskName,
                "spend_hero_attribute_points",
                StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        var current = port.LoadCurrentOptions();
        options = options with
        {
            HeroStatPriority = current.HeroStatPriority,
            HeroStatMaximums = current.HeroStatMaximums,
        };
        return string.Equals(item.TaskName, "hero_manage", StringComparison.OrdinalIgnoreCase)
            ? options with
            {
                HeroMinHpForAdventure = current.HeroMinHpForAdventure,
                HeroAutoRevive = current.HeroAutoRevive,
                HeroAutoAssignPoints = current.HeroAutoAssignPoints,
                HeroAutoUseOintments = current.HeroAutoUseOintments,
                HeroOintmentTargetHpPercent = current.HeroOintmentTargetHpPercent,
                HeroAdventurePickOrder = current.HeroAdventurePickOrder,
                HeroContinuousAdventures = current.HeroContinuousAdventures,
            }
            : options;
    }

    private static string FormatSuccessLog(
        string logPrefix,
        Stopwatch timer,
        QueueItem item,
        AutomationRunMode mode)
    {
        return mode == AutomationRunMode.ContinuousLoop
            ? $"{logPrefix} OK {timer.Elapsed.TotalSeconds:F1}s | queue:{item.TaskName}"
            : $"{logPrefix} OK {timer.Elapsed.TotalSeconds:F1}s task={item.TaskName}";
    }
}
