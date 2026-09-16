using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TbotUltra.Worker.Infrastructure;

namespace TbotUltra.Desktop;

public partial class MainWindow
{
    private bool _windowControlsAdded;
    private Button? _browserVisibilityButton;

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
            Content = "Minimize bot",
            Background = FindResource("ControlBackgroundBrush") as Brush,
            BorderBrush = FindResource("BorderBrush") as Brush,
            Foreground = FindResource("TextSubtleBrush") as Brush,
            ToolTip = "Minimize the Tbot Ultra window to the taskbar.",
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
        WindowState = WindowState.Minimized;
    }
}
