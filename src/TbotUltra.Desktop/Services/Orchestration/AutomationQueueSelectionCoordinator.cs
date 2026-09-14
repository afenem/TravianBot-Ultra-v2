using TbotUltra.Core.Configuration;
using TbotUltra.Worker.Domain;

namespace TbotUltra.Desktop.Services.Orchestration;

internal interface IAutomationQueueSelectionPort
{
    BotOptions LoadOptions();
    void RemoveDisabledUtilityItems(BotOptions options);
    IReadOnlyList<QueueItem> GetQueueItems();
    string? GetVillageKey(QueueItem item);
    string? GetVillageName(QueueItem item);
    bool IsAllowedByAutomationSettings(QueueItem item);
    bool IsUtilityEnabled(string taskName, BotOptions options);
    IReadOnlyList<QueueGroup> GetConsideredGroups();
    VillageBatchSnapshot SnapshotVillageBatch();
    string? ActiveVillageKey { get; }
    string? ActiveVillageName { get; }
    QueueItem? SelectReadyConstruction(
        IReadOnlyList<QueueItem> villageItems,
        DateTimeOffset now,
        bool preview);
    void CompleteUrgentPreemption();
    void RecordUrgentPreemption(string? targetVillageKey);
    string FormatServerTime(DateTimeOffset value);
    void Log(string message);
    void LogVerbose(string message, string key);
}

internal sealed class AutomationQueueSelectionCoordinator(IAutomationQueueSelectionPort port)
{
    internal QueueItem? Select(
        bool preview = false,
        DateTimeOffset? evaluationTimeUtc = null,
        string? villageKeyFilter = null,
        IReadOnlyList<QueueItem>? queueItemsOverride = null)
    {
        var options = port.LoadOptions();
        if (!preview)
        {
            port.RemoveDisabledUtilityItems(options);
        }

        var queueItems = queueItemsOverride ?? port.GetQueueItems();
        var now = evaluationTimeUtc ?? DateTimeOffset.UtcNow;
        var selectionCandidates = queueItems
            .Select(item => new ContinuousLoopSelectionCandidate(
                item,
                port.GetVillageKey(item),
                port.IsAllowedByAutomationSettings(item),
                ContinuousLoopSelector.IsUtilityTask(item.TaskName)
                    && port.IsUtilityEnabled(item.TaskName, options)))
            .Where(candidate => string.IsNullOrWhiteSpace(villageKeyFilter)
                || string.Equals(
                    candidate.VillageKey,
                    villageKeyFilter,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var batch = port.SnapshotVillageBatch();
        var result = AutomationQueueSelector.Select(
            new AutomationQueueSelectionInput(
                selectionCandidates,
                port.GetConsideredGroups(),
                batch,
                port.ActiveVillageKey,
                now,
                options.ShortVillageDeferSeconds,
                preview),
            port.SelectReadyConstruction);
        if (!preview)
        {
            ApplySelectionResult(result, batch, options);
        }

        return result.Selected;
    }

    private void ApplySelectionResult(
        AutomationQueueSelectionResult result,
        VillageBatchSnapshot batch,
        BotOptions options)
    {
        if (result.CompleteUrgentPreemption)
        {
            port.CompleteUrgentPreemption();
            port.Log(
                $"[village-batch] interrupted village key='{batch.VillageKey ?? "-"}' "
                + "has no ready work; urgent preemption completed.");
        }
        else if (result.Reason == AutomationQueueSelectionReason.UrgentPreemption
            && result.Selected is not null
            && !string.Equals(
                port.GetVillageKey(result.Selected),
                port.ActiveVillageKey,
                StringComparison.OrdinalIgnoreCase))
        {
            port.RecordUrgentPreemption(port.GetVillageKey(result.Selected));
            port.Log(
                $"[village-batch] urgent preemption task='{result.Selected.TaskName}' "
                + $"priority={result.Selected.Priority} village='{port.GetVillageName(result.Selected) ?? "-"}'.");
        }
        else if (result.Reason == AutomationQueueSelectionReason.UrgentResume)
        {
            port.Log(
                "[village-batch] urgent work complete; resuming "
                + $"'{port.GetVillageName(result.Selected!) ?? "-"}' "
                + $"with task='{result.Selected!.TaskName}'.");
        }
        else if (result.Reason == AutomationQueueSelectionReason.VillageRotationNoReadyWork)
        {
            port.Log(
                $"[village-batch] complete '{port.ActiveVillageName ?? "-"}' because it has no ready work; "
                + $"next='{port.GetVillageName(result.Selected!) ?? "-"}' "
                + $"task='{result.Selected!.TaskName}'.");
        }
        else if (result.Reason == AutomationQueueSelectionReason.ShortVillageHold)
        {
            port.LogVerbose(
                "[loop-pick:verbose] holding current village for short defer until "
                + $"'{port.FormatServerTime(result.HoldUntil!.Value)}' "
                + $"(limit={options.ShortVillageDeferSeconds}s)",
                $"short-village-hold:{port.ActiveVillageKey}:{result.HoldUntil.Value.UtcTicks}:{options.ShortVillageDeferSeconds}");
        }
        else if (result.Reason == AutomationQueueSelectionReason.NoEnabledGroups)
        {
            port.LogVerbose(
                "[loop-pick:verbose] no enabled groups — nothing to schedule",
                "no-enabled-groups");
        }
        else if (result.Reason == AutomationQueueSelectionReason.NoReadyWork)
        {
            port.LogVerbose(
                $"[loop-pick:verbose] no ready item selected from {result.ConsideredGroupCount} group(s)",
                $"no-selected:{result.ConsideredGroupCount}");
        }
    }
}
