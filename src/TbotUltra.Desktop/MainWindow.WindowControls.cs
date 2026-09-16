using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;
using TbotUltra.Worker.Infrastructure;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private bool _windowControlsAdded;
    private Button? _browserVisibilityButton;
    private Forms.NotifyIcon? _trayIcon;
    private DrawingIcon? _trayIconImage;
    private bool _trayVisible;
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
        if (!_trayVisible)
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
        if (_trayIcon is not null && _trayVisible)
        {
            return;
        }

        if (_trayIcon is null)
        {
            try
            {
                var executablePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    _trayIconImage = DrawingIcon.ExtractAssociatedIcon(executablePath);
                }
            }
            catch
            {
                _trayIconImage = null;
            }

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application,
                Text = "Tbot Ultra",
                Visible = false,
            };

            var menu = new Forms.ContextMenuStrip();
            var showItem = new Forms.ToolStripMenuItem("Tbot Ultra'yı Göster");
            showItem.Click += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
            var exitItem = new Forms.ToolStripMenuItem("Tbot Ultra'yı Kapat");
            exitItem.Click += (_, _) =>
            {
                _exiting = true;
                _trayIcon!.Visible = false;
                Application.Current.Shutdown();
            };
            menu.Items.Add(showItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
        }

        _trayIcon.Visible = true;
        _trayVisible = true;
    }

    private void CleanupTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
        _trayVisible = false;

        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    private void MainWindow_Closed(object? sender, System.EventArgs e)
    {
        CleanupTrayIcon();
    }
}
