using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TbotUltra.Worker.Infrastructure;

/// <summary>
/// Controls visibility of Tbot-owned browser windows without touching the user's normal Chrome/Edge windows.
/// Ownership is taken only from LaunchedBrowserRegistry's PID + start-time + executable-path records.
/// </summary>
public static class TrackedBrowserWindowHider
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(350);
    private static readonly JsonSerializerOptions SerializerOptions = new();
    private static volatile bool _hidden = true;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private sealed record TrackedBrowser(
        [property: JsonPropertyName("pid")] int Pid,
        [property: JsonPropertyName("startedAtUtcTicks")] long StartedAtUtcTicks,
        [property: JsonPropertyName("executablePath")] string ExecutablePath);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static bool IsHidden => _hidden;

    public static void SetHidden(bool hidden)
    {
        _hidden = hidden;
        ApplyToTrackedWindows();
    }

    public static void Toggle()
    {
        SetHidden(!_hidden);
    }

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
        var registryPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config", "cache", "launched-browsers.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "config", "cache", "launched-browsers.json"),
        };

        while (true)
        {
            try
            {
                foreach (var registryPath in registryPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ApplyToTrackedWindows(registryPath);
                }
            }
            catch
            {
                // Visibility control is best-effort. Automation must never fail because of this.
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

    private static void ApplyToTrackedWindows()
    {
        var registryPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config", "cache", "launched-browsers.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "config", "cache", "launched-browsers.json"),
        };

        foreach (var registryPath in registryPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ApplyToTrackedWindows(registryPath);
        }
    }

    private static void ApplyToTrackedWindows(string registryPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(registryPath))
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

                if (process.StartTime.ToUniversalTime().Ticks != entry.StartedAtUtcTicks)
                {
                    continue;
                }

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

                ApplyToAllProcessWindows(entry.Pid, _hidden ? SwHide : SwShow);
            }
            catch
            {
                // Process may have exited or its native window may not be available yet.
            }
            finally
            {
                process?.Dispose();
            }
        }
    }

    private static void ApplyToAllProcessWindows(int pid, int command)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        EnumWindows((hWnd, _) =>
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out var windowPid);
                if (windowPid == (uint)pid)
                {
                    ShowWindow(hWnd, command);
                }
            }
            catch
            {
                // A window can disappear during enumeration.
            }

            return true;
        }, IntPtr.Zero);
    }
}
