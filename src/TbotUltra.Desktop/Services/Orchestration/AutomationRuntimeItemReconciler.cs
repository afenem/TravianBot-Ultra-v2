using TbotUltra.Core.Configuration;
using TbotUltra.Worker.Domain;

namespace TbotUltra.Desktop.Services.Orchestration;

internal sealed record AutomationRuntimeItemSpec(
    string TaskName,
    string DisplayName,
    Dictionary<string, string>? Payload,
    int Priority,
    int MaxRetries,
    string? VillageKey = null,
    bool RefreshPendingPayload = false,
    bool RefreshPendingPriority = false);

internal enum AutomationRuntimeItemChange
{
    None,
    Added,
    PayloadUpdated,
    PriorityUpdated,
    PayloadAndPriorityUpdated,
}

internal readonly record struct AutomationRuntimeItemResult(
    QueueItem Item,
    AutomationRuntimeItemChange Change);

internal interface IAutomationRuntimeQueuePort
{
    QueueItem Enqueue(AutomationRuntimeItemSpec spec);

    bool UpdatePendingPayload(Guid id, Dictionary<string, string> payload);

    bool UpdatePendingPriority(Guid id, int priority);
}

internal sealed class AutomationRuntimeItemReconciler(
    IReadOnlyList<QueueItem> queueItems,
    Func<string?, string?> canonicalizeVillageKey,
    IAutomationRuntimeQueuePort queue)
{
    private readonly List<QueueItem> _activeItems = queueItems
        .Where(item => item.Status is QueueStatus.Pending or QueueStatus.Running or QueueStatus.Paused)
        .ToList();

    internal bool HasActive(string taskName) =>
        FindActive(taskName, villageKey: null) is not null;

    internal bool HasActiveForVillage(string taskName, string villageKey) =>
        FindActive(taskName, villageKey) is not null;

    internal QueueItem? FindPendingForVillage(string taskName, string villageKey) =>
        FindActive(taskName, villageKey) is { Status: QueueStatus.Pending } item ? item : null;

    internal AutomationRuntimeItemResult Ensure(AutomationRuntimeItemSpec spec)
    {
        var existing = FindActive(spec.TaskName, spec.VillageKey);
        if (existing is null)
        {
            var added = queue.Enqueue(spec);
            _activeItems.Add(added);
            return new AutomationRuntimeItemResult(added, AutomationRuntimeItemChange.Added);
        }

        if (existing.Status != QueueStatus.Pending)
        {
            return new AutomationRuntimeItemResult(existing, AutomationRuntimeItemChange.None);
        }

        var payloadUpdated = spec.RefreshPendingPayload
            && spec.Payload is not null
            && !ContinuousLoopSelector.PayloadEquals(existing.Payload, spec.Payload)
            && queue.UpdatePendingPayload(existing.Id, spec.Payload);
        var priorityUpdated = spec.RefreshPendingPriority
            && existing.Priority != spec.Priority
            && queue.UpdatePendingPriority(existing.Id, spec.Priority);
        var change = (payloadUpdated, priorityUpdated) switch
        {
            (true, true) => AutomationRuntimeItemChange.PayloadAndPriorityUpdated,
            (true, false) => AutomationRuntimeItemChange.PayloadUpdated,
            (false, true) => AutomationRuntimeItemChange.PriorityUpdated,
            _ => AutomationRuntimeItemChange.None,
        };
        return new AutomationRuntimeItemResult(existing, change);
    }

    internal QueueItem EnqueueNew(AutomationRuntimeItemSpec spec)
    {
        var item = queue.Enqueue(spec);
        _activeItems.Add(item);
        return item;
    }

    private QueueItem? FindActive(string taskName, string? villageKey)
    {
        var canonicalVillageKey = canonicalizeVillageKey(villageKey) ?? villageKey ?? string.Empty;
        return _activeItems.FirstOrDefault(item =>
            string.Equals(item.TaskName, taskName, StringComparison.OrdinalIgnoreCase)
            && (villageKey is null
                || string.Equals(
                    canonicalizeVillageKey(item.Payload.GetValueOrDefault(BotOptionPayloadKeys.TargetVillageKey))
                        ?? item.Payload.GetValueOrDefault(BotOptionPayloadKeys.TargetVillageKey)
                        ?? string.Empty,
                    canonicalVillageKey,
                    StringComparison.OrdinalIgnoreCase)));
    }
}
