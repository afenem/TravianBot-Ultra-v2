using TbotUltra.Desktop.Services;
using TbotUltra.Desktop.Services.Orchestration;
using TbotUltra.Worker.Domain;
using Xunit;

namespace TbotUltra.Desktop.Tests;

public sealed class ContinuousAutomationForecastCoordinatorTests
{
    [Fact]
    public void Resolve_UsesQueueDeadlineAndTheSamePreviewSelectionSeam()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var item = new QueueItem
        {
            TaskName = "hero_manage",
            Group = QueueGroup.Hero,
            Status = QueueStatus.Pending,
            NextAttemptAt = now.AddMinutes(3),
        };
        var port = new InMemoryPort(item);

        var forecast = new ContinuousAutomationForecastCoordinator(port).Resolve(now);

        Assert.Equal(ContinuousLoopForecastState.Waiting, forecast.State);
        Assert.Same(item, forecast.Item);
        Assert.Equal(item.NextAttemptAt, forecast.ReadyAtUtc);
    }

    private sealed class InMemoryPort(QueueItem item) : IContinuousAutomationForecastPort
    {
        public IReadOnlyList<QueueItem> GetQueueItems() => [item];
        public string? GetVillageKey(QueueItem candidate) => null;
        public bool IsAllowedByAutomationSettings(QueueItem candidate) => true;
        public TimeSpan? ResolveConstructionQueueDelay(QueueItem candidate, DateTimeOffset now) => null;
        public TimeSpan? ResolveConstructPrerequisiteDelay(QueueItem candidate, DateTimeOffset now) => null;
        public QueueItem? SelectPreview(
            DateTimeOffset evaluationTime,
            string? villageKeyFilter,
            IReadOnlyList<QueueItem> queueItems) =>
            evaluationTime >= item.NextAttemptAt ? item : null;
        public bool HasKnownConstructionAvailability(QueueItem candidate, DateTimeOffset now) => true;
    }
}
