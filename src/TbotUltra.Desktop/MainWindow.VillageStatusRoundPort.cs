using TbotUltra.Core.Configuration;
using TbotUltra.Desktop.Models;
using TbotUltra.Desktop.Services.Orchestration;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private sealed class MainWindowVillageStatusRoundPort(
        MainWindow owner,
        BotOptions options,
        IReadOnlyDictionary<string, VillageSelectionItem> villagesByKey,
        int villageCount) : IVillageStatusRoundPort
    {
        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            try
            {
                await owner.EnsureContinuousLoopRuntimeItemsAsync(options, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                owner.AppendLog(
                    "[village-scan] runtime preparation failed; existing queued work will still run: "
                    + FormatExceptionForLog(ex));
            }

            owner.AppendLog($"[village-scan] starting round for {villageCount} village(s).");
        }

        public ValueTask<VillageStatusRoundVisitResult> VisitAsync(
            VillageStatusRoundVillage village,
            int villageNumber,
            int totalVillageCount,
            bool inboxStatusChecked,
            CancellationToken cancellationToken) =>
            owner.VisitVillageStatusRoundAsync(
                options,
                villagesByKey[village.Key],
                villageNumber,
                totalVillageCount,
                inboxStatusChecked,
                cancellationToken);

        public ValueTask DelayBeforeNextVillageAsync(CancellationToken cancellationToken) =>
            new(ActionPacer.FromOptions(options, owner.AppendLog).DelayAsync(
                options.VillageStatusSweepVillageMinSeconds,
                options.VillageStatusSweepVillageMaxSeconds,
                cancellationToken,
                "Village scan: next village"));
    }
}
