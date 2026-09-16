using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TbotUltra.Worker.Infrastructure;

/// <summary>
/// Keeps Tbot-owned browser windows hidden without touching the user's normal Chrome/Edge windows.
/// Ownership is taken only from LaunchedBrowserRegistry's PID + start-time + executable-path records.
/// The helper runs only on Windows and is intentionally a separate background monitor so a headed
/// Playwright browser can remain fully functional while its native window stays invisible.
/// </summary>
internal static class TrackedBrowserWindowHider
{
    private const int SwHide = 0;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(350);
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private sealed record TrackedBrowser(
        [property: JsonPropertyName("pid")] int Pid,
        [property: JsonPropertyName("startedAtUtcTicks")] long StartedAtUtcTicks,
        [property: JsonPropertyName("executablePath")] string ExecutablePath);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = MonitorAsync();
    }

    private static async Task MonitorAsync()
    {
        var registryPath = Path.Combine(
            AppContext.BaseDirectory,
            "config",
            "cache",
            "launched-browsers.json");

        while (true)
        {
            try
            {
                HideTrackedWindows(registryPath);
            }
            catch
            {
                // Hiding is best-effort. A browser that has not created its window yet will be
                // retried on the next polling interval; automation must never fail because of this.
            }

            try
            {
                await Task.Delay(PollInterval).ConfigureAwait(false);
            }
            catch
            {
                return;
            }
        }
    }

    private static void HideTrackedWindows(string registryPath)
    {
        if (!File.Exists(registryPath))
        {
            return;
        }

        List<TrackedBrowser>? browsers;
        try
        {
            browsers = JsonSerializer.Deserialize<List<TrackedBrowser>>(
                File.ReadAllText(registryPath),
                SerializerOptions);
        }
        catch
        {
            return;
        }

        if (browsers is null || browsers.Count == 0)
        {
            return;
        }

        foreach (var entry in browsers)
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(entry.Pid);

                // Windows can reuse a PID. The recorded start time must still match exactly.
                if (process.StartTime.ToUniversalTime().Ticks != entry.StartedAtUtcTicks)
                {
                    continue;
                }

                // The executable path must still be the same process we originally recorded.
                string? executablePath;
                try
                {
                    executablePath = process.MainModule?.FileName;
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(executablePath, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var windowHandle = process.MainWindowHandle;
                if (windowHandle == IntPtr.Zero)
                {
                    continue;
                }

                ShowWindow(windowHandle, SwHide);
            }
            catch
            {
                // Process may have exited or the window may not be available yet.
            }
            finally
            {
                process?.Dispose();
            }
        }
    }
}
