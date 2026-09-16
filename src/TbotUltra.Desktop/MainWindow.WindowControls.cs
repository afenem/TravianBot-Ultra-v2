using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TbotUltra.Worker.Infrastructure;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private bool _windowControlsAdded;
    private Button? _browserVisibilityButton;
    private HwndSource? _trayHwndSource;
    private IntPtr _trayHwnd;
    private bool _trayHookAdded;
    private bool _trayIconVisible;

    private const int TrayCallbackMessage = 0x8001;
    private const int WmLButtonDblClk = 0x0203;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint WmGetIcon = 0x007F;
    private const IntPtr IconSmall2 = 2;
    private const IntPtr IconSmall = 0;
    private const int GclpHicon = -14;
    private const int GclpHiconSm = -34;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.AddWindowControlButtons();
        }
    }

    private void AddWindowControlButtons()
    {
        if (_windowControlsAdded)
        {
            return;
        }

        if (SettingsButton.Parent is not Grid settingsGrid
            || settingsGrid.Parent is not StackPanel bottomPanel)
        {
            return;
        }

        var controlGrid = new Grid
        {
            Name = "BotWindowControlGrid",
            Margin = new Thickness(0, 0, 0, 6),
        };
        controlGrid.ColumnDefinitions.Add(new ColumnDefinition());
        controlGrid.ColumnDefinitions.Add(new ColumnDefinition());

        _browserVisibilityButton = new Button
        {
            Height = 30,
            Margin = new Thickness(0, 0, 3, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Background = FindResource("InfoBgBrush") as Brush,
            BorderBrush = FindResource("FocusBorderBrush") as Brush,
            Foreground = FindResource("InfoTextBrush") as Brush,
        };
        _browserVisibilityButton.Click += BrowserVisibilityButton_Click;
        Grid.SetColumn(_browserVisibilityButton, 0);
        controlGrid.Children.Add(_browserVisibilityButton);

        var minimizeButton = new Button
        {
            Height = 30,
            Margin = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Content = "Minimize to tray",
            Background = FindResource("ControlBackgroundBrush") as Brush,
            BorderBrush = FindResource("BorderBrush") as Brush,
            Foreground = FindResource("TextSubtleBrush") as Brush,
            ToolTip = "Hide Tbot Ultra in the Windows notification area while it keeps running.",
        };
        minimizeButton.Click += MinimizeBotButton_Click;
        Grid.SetColumn(minimizeButton, 1);
        controlGrid.Children.Add(minimizeButton);

        var settingsIndex = bottomPanel.Children.IndexOf(settingsGrid);
        if (settingsIndex < 0)
        {
            return;
        }

        bottomPanel.Children.Insert(settingsIndex, controlGrid);
        _windowControlsAdded = true;
        Closed += (_, _) => CleanupTrayIcon();
        UpdateBrowserVisibilityButton();
    }

    private void BrowserVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        TrackedBrowserWindowHider.Toggle();
        UpdateBrowserVisibilityButton();
        AppendLog(TrackedBrowserWindowHider.IsHidden
            ? "[browser-ui] Bot browser windows hidden."
            : "[browser-ui] Bot browser windows shown.");
    }

    private void UpdateBrowserVisibilityButton()
    {
        if (_browserVisibilityButton is null)
        {
            return;
        }

        var hidden = TrackedBrowserWindowHider.IsHidden;
        _browserVisibilityButton.Content = hidden ? "Show Chrome" : "Hide Chrome";
        _browserVisibilityButton.ToolTip = hidden
            ? "Show Chrome windows launched by Tbot Ultra."
            : "Hide Chrome windows launched by Tbot Ultra.";
    }

    private void MinimizeBotButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        EnsureTrayIcon();
        if (!_trayIconVisible)
        {
            AppendLog("[ui] Could not create the notification-area icon.");
            return;
        }

        ShowInTaskbar = false;
        Hide();
        AppendLog("[ui] Tbot Ultra minimized to the notification area.");
    }

    private void RestoreFromTray()
    {
        RemoveTrayIcon();
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        UpdateBrowserVisibilityButton();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIconVisible)
        {
            return;
        }

        _trayHwnd = new WindowInteropHelper(this).EnsureHandle();
        _trayHwndSource ??= HwndSource.FromHwnd(_trayHwnd);
        if (_trayHwndSource is null)
        {
            return;
        }

        if (!_trayHookAdded)
        {
            _trayHwndSource.AddHook(TrayWndProc);
            _trayHookAdded = true;
        }

        var iconHandle = GetWindowIconHandle();
        if (iconHandle == IntPtr.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData(iconHandle);
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            return;
        }

        data.uTimeoutOrVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref data);
        _trayIconVisible = true;
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconVisible || _trayHwnd == IntPtr.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData(GetWindowIconHandle());
        Shell_NotifyIcon(NimDelete, ref data);
        _trayIconVisible = false;
    }

    private NotifyIconData CreateNotifyIconData(IntPtr iconHandle)
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _trayHwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = iconHandle,
            szTip = "Tbot Ultra — double-click to restore",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private IntPtr GetWindowIconHandle()
    {
        var handle = SendMessage(_trayHwnd, WmGetIcon, IconSmall2, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        handle = SendMessage(_trayHwnd, WmGetIcon, IconSmall, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        handle = GetClassLongPtr(_trayHwnd, GclpHiconSm);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        return GetClassLongPtr(_trayHwnd, GclpHicon);
    }

    private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TrayCallbackMessage)
        {
            var mouseMessage = unchecked((int)lParam.ToInt64());
            if (mouseMessage == WmLButtonDblClk)
            {
                Dispatcher.BeginInvoke(RestoreFromTray);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void CleanupTrayIcon()
    {
        RemoveTrayIcon();
        if (_trayHwndSource is not null && _trayHookAdded)
        {
            _trayHwndSource.RemoveHook(TrayWndProc);
            _trayHookAdded = false;
        }
    }
}
