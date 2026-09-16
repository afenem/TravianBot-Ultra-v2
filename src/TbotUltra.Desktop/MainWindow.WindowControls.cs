using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using TbotUltra.Worker.Infrastructure;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private bool _windowControlsAdded;
    private Button? _browserVisibilityButton;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Icon? _trayIconImage;

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
            window.InitializeTrayIcon();
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
            ToolTip = "Hide Tbot Ultra and keep it running in the Windows notification area.",
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
        UpdateBrowserVisibilityButton();
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        try
        {
            var resourceInfo = Application.GetResourceStream(new Uri("/Assets/icon_windows.ico", UriKind.Relative));
            if (resourceInfo is not null)
            {
                using var iconStream = resourceInfo.Stream;
                _trayIconImage = new Icon(iconStream);
            }
        }
        catch
        {
            _trayIconImage = null;
        }

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("Show Tbot Ultra", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        _trayMenu.Items.Add("Hide Chrome", null, (_, _) => Dispatcher.Invoke(() =>
        {
            TrackedBrowserWindowHider.SetHidden(true);
            UpdateBrowserVisibilityButton();
        }));
        _trayMenu.Items.Add("Show Chrome", null, (_, _) => Dispatcher.Invoke(() =>
        {
            TrackedBrowserWindowHider.SetHidden(false);
            UpdateBrowserVisibilityButton();
        }));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(CloseFromTray));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Visible = true,
            Text = "Tbot Ultra",
            ContextMenuStrip = _trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
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
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
        }

        Hide();
        AppendLog("[ui] Tbot Ultra minimized to the notification area.");
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void CloseFromTray()
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        base.OnClosed(e);
    }
}
