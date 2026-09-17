using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using TbotUltra.Worker.Infrastructure;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private const int TrayCallbackMessage = 0x8001;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint WmGetIcon = 0x007F;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint WmNull = 0x0000;
    private const uint TrayMenuShow = 1001;
    private const uint TrayMenuExit = 1002;

    private static readonly IntPtr IconSmall2 = new(2);
    private static readonly IntPtr IconSmall = IntPtr.Zero;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hWnd,
        IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private bool _windowControlsAdded;
    private Button? _browserVisibilityButton;
    private HwndSource? _trayHwndSource;
    private IntPtr _trayHwnd;
    private bool _trayHookAdded;
    private bool _trayIconVisible;
    private bool _exiting;

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
            Background = FindResource("InfoBgBrush") as System.Windows.Media.Brush,
            BorderBrush = FindResource("FocusBorderBrush") as System.Windows.Media.Brush,
            Foreground = FindResource("InfoTextBrush") as System.Windows.Media.Brush,
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
            Background = FindResource("ControlBackgroundBrush") as System.Windows.Media.Brush,
            BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush,
            Foreground = FindResource("TextSubtleBrush") as System.Windows.Media.Brush,
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
        Closed += MainWindow_Closed;
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
        AppendLog("[ui] Tbot Ultra minimized to the notification area. Right-click or double-click its tray icon to restore.");
    }

    private void RestoreFromTray()
    {
        if (_exiting)
        {
            return;
        }

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

        _trayHwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        _trayHwndSource ??= System.Windows.Interop.HwndSource.FromHwnd(_trayHwnd);
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
            szTip = "Tbot Ultra",
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

        handle = GetClassLongPtr(_trayHwnd, -34);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        return GetClassLongPtr(_trayHwnd, -14);
    }

    private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMessage = unchecked((int)lParam.ToInt64());
        if (mouseMessage == WmLButtonDblClk)
        {
            Dispatcher.BeginInvoke(RestoreFromTray);
            handled = true;
        }
        else if (mouseMessage == WmRButtonUp)
        {
            Dispatcher.BeginInvoke(ShowTrayContextMenu);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowTrayContextMenu()
    {
        if (!_trayIconVisible || _trayHwnd == IntPtr.Zero || _exiting)
        {
            return;
        }

        if (!GetCursorPos(out var point))
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, new UIntPtr(TrayMenuShow), "Tbot Ultra'yı Göster");
            AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenu(menu, MfString, new UIntPtr(TrayMenuExit), "Tbot Ultra'yı Kapat");

            SetForegroundWindow(_trayHwnd);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                _trayHwnd,
                IntPtr.Zero);

            if (command == TrayMenuShow)
            {
                RestoreFromTray();
            }
            else if (command == TrayMenuExit)
            {
                ExitFromTray();
            }
        }
        finally
        {
            DestroyMenu(menu);
            PostMessage(_trayHwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void ExitFromTray()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        CleanupTrayIcon();
        ShowInTaskbar = true;
        Application.Current.Shutdown();
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

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        CleanupTrayIcon();
    }
}
