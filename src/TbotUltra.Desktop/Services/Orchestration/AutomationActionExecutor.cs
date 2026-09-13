using TbotUltra.Core.Configuration;
using TbotUltra.Worker.Domain;

namespace TbotUltra.Desktop.Services.Orchestration;

internal interface IAutomationActionExecutionPort
{
    long ContinuousPassId { get; }
    long AutoQueueRunLogId { get; }
    bool LoopStopRequested { get; }
    bool QueueStopRequested { get; }
    QueueItem? FindQueueItem(Guid id);
    BotOptions LoadOptions();
    ValueTask EnsureChromiumInstalledAsync();
    void RecordVillageBatchAttempt(QueueItem item, string source);
    ValueTask<bool> ExecuteQueueItemAsync(
        QueueItem item,
        BotOptions options,
        AutomationRunMode mode,
        string logPrefix,
        CancellationToken cancellationToken);
    void MarkContinuousBrowserActivity(BotOptions options);
    ValueTask ApplyPostTaskCooldownAsync(
        QueueItem item,
        BotOptions options,
        CancellationToken cancellationToken);
    void Log(string message);
}

internal sealed class AutomationActionExecutor(IAutomationActionExecutionPort port)
{
    internal async ValueTask<AutomationActionOutcome> ExecuteAsync(
        AutomationRunMode mode,
        AutomationCandidate action,
        CancellationToken cancellationToken)
    {
        var item = port.FindQueueItem(action.Id);
        var runId = mode == AutomationRunMode.ContinuousLoop
            ? port.ContinuousPassId
            : port.AutoQueueRunLogId;
        var source = mode == AutomationRunMode.ContinuousLoop ? $"LOOP {runId}" : $"AUTOQ {runId}";
        var logPrefix = $"[{source}]";
        if (item is null)
        {
            port.Log($"{logPrefix} SKIP missing queue item id={action.Id}");
            return AutomationActionOutcome.Skipped;
        }

        if (mode == AutomationRunMode.AutoQueue)
        {
            await port.EnsureChromiumInstalledAsync();
        }

        var options = AutomationExecutionOptions.WithoutImplicitVillageTarget(port.LoadOptions());
        if (mode == AutomationRunMode.ContinuousLoop && port.LoopStopRequested)
        {
            return AutomationActionOutcome.Blocked;
        }

        if (mode == AutomationRunMode.ContinuousLoop)
        {
            await ActionPacer.FromOptions(options, port.Log).DelayAsync(
                options.ActionPacingTaskMinSeconds,
                options.ActionPacingTaskMaxSeconds,
                cancellationToken,
                "before task");
        }
        else
        {
            port.Log($"{logPrefix} RUN task={item.TaskName}, id={item.Id}");
        }

        port.RecordVillageBatchAttempt(item, source);
        var shouldContinue = await port.ExecuteQueueItemAsync(
            item,
            options,
            mode,
            logPrefix,
            cancellationToken);
        if (mode == AutomationRunMode.ContinuousLoop)
        {
            port.MarkContinuousBrowserActivity(options);
        }
        if (!shouldContinue)
        {
            return AutomationActionOutcome.Blocked;
        }

        var stopRequested = mode == AutomationRunMode.ContinuousLoop
            ? port.LoopStopRequested
            : port.QueueStopRequested;
        if (stopRequested)
        {
            return AutomationActionOutcome.Completed;
        }

        await port.ApplyPostTaskCooldownAsync(item, options, cancellationToken);
        return AutomationActionOutcome.Completed;
    }
}
