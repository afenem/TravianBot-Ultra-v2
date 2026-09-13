using TbotUltra.Desktop.Services.Orchestration;
using TbotUltra.Worker.Domain;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private sealed class MainWindowAutomationRuntimeQueuePort(MainWindow owner)
        : IAutomationRuntimeQueuePort
    {
        public QueueItem Enqueue(AutomationRuntimeItemSpec spec) => owner._botService.EnqueueRuntime(
            spec.TaskName,
            spec.DisplayName,
            spec.Payload,
            spec.Priority,
            spec.MaxRetries);

        public bool UpdatePendingPayload(Guid id, Dictionary<string, string> payload) =>
            owner._botService.UpdateDeferredQueueItem(id, payload);

        public bool UpdatePendingPriority(Guid id, int priority) =>
            owner._botService.UpdatePendingQueueItem(id, payload: null, priority: priority);
    }
}
