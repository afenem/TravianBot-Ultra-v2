using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using TbotUltra.Core.Travian;
using TbotUltra.Desktop.Models;
using TbotUltra.Worker.Domain;
using TbotUltra.Worker.Services;

namespace TbotUltra.Desktop;

public partial class CatapultWaveWindow : Window
{
    private readonly IReadOnlyList<string> _troopTypes;
    private readonly Dictionary<string, long> _availableTroops;
    private int? _rallyPointLevel;
    private readonly Dictionary<string, TextBox> _firstAttackInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _waveInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Run> _firstAttackAmountRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Hyperlink> _firstAttackAmountLinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Run> _waveAmountRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<VillageSelectionItem> _villages;
    private VillageSelectionItem? _activeVillage;
    private readonly CancellationTokenSource _windowCts = new();
    private bool _suppressRefresh;
    private bool _isRunning;
    private bool _isRefreshing;

    public Func<CatapultWaveRequest, Action<string>, Func<int, CancellationToken, Task<bool>>, CancellationToken, Task<CatapultWaveRunResult>>? StartRequested { get; init; }
    public Func<Action<string>, CancellationToken, Task<CatapultWaveSetupInfo>>? RefreshRequested { get; init; }

    /// <summary>
    /// Optional first-time load run automatically when the window opens. While it runs the busy
    /// overlay is shown so the popup never appears empty/unresponsive. If null, the window opens
    /// with whatever troops were passed to the constructor.
    /// </summary>
    public Func<Action<string>, CancellationToken, Task<CatapultWaveSetupInfo>>? InitialLoadRequested { get; init; }
    public Func<VillageSelectionItem, Action<string>, CancellationToken, Task<CatapultWaveSetupInfo>>? SwitchVillageRequested { get; init; }

    public CatapultWaveWindow(
        string tribe,
        IReadOnlyDictionary<string, long>? availableTroops = null,
        int? rallyPointLevel = null,
        IReadOnlyList<VillageSelectionItem>? villages = null,
        VillageSelectionItem? activeVillage = null)
    {
        InitializeComponent();
        ThemeChrome.EnableEarlyDarkTitleBar(this);
        _troopTypes = TroopCatalog.ResolveTroopTypesForTribe(tribe);
        ConfigureFirstAttackTargetPickers(tribe);
        _availableTroops = availableTroops is null
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(availableTroops, StringComparer.OrdinalIgnoreCase);
        _rallyPointLevel = rallyPointLevel;
        _villages = villages ?? [];
        _activeVillage = activeVillage;
        VillageComboBox.ItemsSource = _villages;
        VillageComboBox.SelectedItem = activeVillage ?? _villages.FirstOrDefault();
        ConfigureZeroDefaultTextBox(XTextBox);
        ConfigureZeroDefaultTextBox(YTextBox);
        BuildTroopGrid(FirstAttackTroopsGrid, _firstAttackInputs, isFirstAttackGrid: true);
        BuildTroopGrid(WaveTroopsGrid, _waveInputs, isFirstAttackGrid: false);
        RefreshRallyPointLevelText();
        RefreshUiState();
        RefreshSwitchVillageState();
        Loaded += OnWindowLoaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnWindowLoaded;
        _windowCts.Cancel();
        _windowCts.Dispose();
        base.OnClosed(e);
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        => await AsyncUi.GuardAsync(() => OnWindowLoadedAsync(sender, e), LogUiGuardError);

    private async Task OnWindowLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        if (InitialLoadRequested is null)
        {
            BusyOverlay.Hide();
            return;
        }

        SetRefreshing(true);
        BusyOverlay.Show("Catapult waves", "Reading troops from Rally Point…");
        try
        {
            var setupInfo = await InitialLoadRequested(message => SetStatus(message, isAlarm: false), _windowCts.Token);
            UpdateSetupInfo(setupInfo);
            SetStatus("Troops loaded from Rally Point.", isAlarm: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Loading canceled.", isAlarm: true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isAlarm: true);
        }
        finally
        {
            BusyOverlay.Hide();
            SetRefreshing(false);
        }
    }

    #region UI building

    private void BuildTroopGrid(
        Grid grid,
        Dictionary<string, TextBox> inputs,
        bool isFirstAttackGrid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        for (var i = 0; i < _troopTypes.Count; i++)
        {
            var troopType = _troopTypes[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = troopType,
                Margin = new Thickness(0, i == 0 ? 0 : 6, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ThemeColors.Get("TextPrimaryBrush")),
            };

            label.Inlines.Clear();
            label.Inlines.Add(new Run(troopType));
            label.Inlines.Add(new Run(" "));

            var amountRun = new Run("(0)");
            if (isFirstAttackGrid)
            {