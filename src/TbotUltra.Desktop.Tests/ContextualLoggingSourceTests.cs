using Xunit;

namespace TbotUltra.Desktop.Tests;

public sealed class ContextualLoggingSourceTests
{
    [Fact]
    public void DesktopLogPipeline_ParsesRawMessageButWritesContextualHumanLine()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "TbotUltra.Desktop",
            "MainWindow.Logging.Stream.cs"));

        Assert.Contains("AutomationLogContext.Capture()", source, StringComparison.Ordinal);
        Assert.Contains("AutomationLogContext.FormatForHuman(part, pending.Context)", source, StringComparison.Ordinal);
        Assert.Contains("TryApplyInlineResourceLevelUpdateFromLog(part)", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TbotUltra.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
