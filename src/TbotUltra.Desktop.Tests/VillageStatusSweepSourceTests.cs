using TbotUltra.Worker;
using Xunit;

namespace TbotUltra.Desktop.Tests;

public sealed class VillageStatusSweepSourceTests
{
    [Fact]
    public void ScanNowButton_IsBoundToTheVillageStatusRoundCommand()
    {
        var projectRoot = ProjectRootLocator.FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(
            projectRoot,
            "src",
            "TbotUltra.Desktop",
            "SettingsWindow.xaml"));

        Assert.Contains("Content=\"Scan now\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SettingsVm.RunVillageStatusSweepNowCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DorfTooltips_DescribeTheirActualScanResponsibilities()
    {
        var projectRoot = ProjectRootLocator.FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(
            projectRoot,
            "src",
            "TbotUltra.Desktop",
            "SettingsWindow.xaml"));

        Assert.Contains(
            "Reads resources, hourly production, Warehouse and Granary capacity, population, construction queue, tasks, daily quests, unread messages and reports.",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Reads all buildings in the village center. Required before Smithy, Barracks, Stable, Workshop, Town Hall or Brewery can be scanned.",
            xaml,
            StringComparison.Ordinal);
    }
}
