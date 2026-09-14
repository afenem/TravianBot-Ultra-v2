using TbotUltra.Core.Configuration;
using TbotUltra.Desktop.Services;
using TbotUltra.Desktop.Services.Orchestration;
using TbotUltra.Worker.Domain;
using Xunit;

namespace TbotUltra.Desktop.Tests;

public sealed class AutomationQueueSelectionCoordinatorTests
{
    [Fact]
    public void Select_UsesLiveCandidateAndAppliesPreparationThroughTheSeam()
    {
        var item = new QueueItem
        {
            Id = Guid.NewGuid(),
            TaskName = "hero_manage",
            Group = QueueGroup.Hero,
            Status = QueueStatus.Pending,
            NextAttemptAt = DateTimeOffset.MinValue,
        };
        var port = new InMemoryPort { Items = [item], Groups = [QueueGroup.Hero] };

        var selected = new AutomationQueueSelectionCoordinator(port).Select();

        Assert.Same(item, selected);
        Assert.Equal(1, port.RemoveDisabledUtilityItemsCount);
    }

    [Fact]
    public void Preview_DoesNotMutateUtilityItemsOrPublishDecisionLogs()
    {
        var port = new InMemoryPort();

        var selected = new AutomationQueueSelectionCoordinator(port).Select(preview: true);

        Assert.Null(selected);
        Assert.Equal(0, port.RemoveDisabledUtilityItemsCount);
        Assert.Empty(port.VerboseLogs);
    }

    private sealed class InMemoryPort : IAutomationQueueSelectionPort
    {
        public IReadOnlyList<QueueItem> Items { get; init; } = [];
        public IReadOnlyList<QueueGroup> Groups { get; init; } = [];
        public int RemoveDisabledUtilityItemsCount { get; private set; }
        public List<string> VerboseLogs { get; } = [];
        public string? ActiveVillageKey => null;
        public string? ActiveVillageName => null;
        public BotOptions LoadOptions() => new();
        public void RemoveDisabledUtilityItems(BotOptions options) =>
            RemoveDisabledUtilityItemsCount++;
        public IReadOnlyList<QueueItem> GetQueueItems() => Items;
        public string? GetVillageKey(QueueItem item) => null;
        public string? GetVillageName(QueueItem item) => null;
        public bool IsAllowedByAutomationSettings(QueueItem item) => true;
        public bool IsUtilityEnabled(string taskName, BotOptions options) => true;
        public IReadOnlyList<QueueGroup> GetConsideredGroups() => Groups;
        public VillageBatchSnapshot SnapshotVillageBatch() => default;
        public QueueItem? SelectReadyConstruction(
            IReadOnlyList<QueueItem> villageItems,
            DateTimeOffset now,
            bool preview) => villageItems.FirstOrDefault();
        public void CompleteUrgentPreemption() { }
        public void RecordUrgentPreemption(string? targetVillageKey) { }
        public string FormatServerTime(DateTimeOffset value) => value.ToString("O");
        public void Log(string message) { }
        public void LogVerbose(string message, string key) => VerboseLogs.Add(message);
    }
}
