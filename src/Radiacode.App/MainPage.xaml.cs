using Microsoft.UI.Xaml.Media.Animation;
using Radiacode.App.ViewModels;

namespace Radiacode.App;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    private readonly Dictionary<string, FrameworkElement> _pages;
    private readonly Button[] _navButtons;

    public MainPage()
    {
        this.InitializeComponent();

        SpectrumPage.ViewModel = ViewModel;
        HistoryPage.ViewModel = ViewModel;
        EventsPage.ViewModel = ViewModel;
        DevicePage.ViewModel = ViewModel;
        SettingsPage.ViewModel = ViewModel;

        _pages = new()
        {
            ["Overview"] = OverviewPage,
            ["Spectrum"] = SpectrumPage,
            ["History"] = HistoryPage,
            ["Events"] = EventsPage,
            ["Device"] = DevicePage,
            ["Settings"] = SettingsPage,
        };
        _navButtons = [NavOverview, NavSpectrum, NavHistory, NavEvents, NavDevice, NavSettings];

        // Ambient Cherenkov glow breathes while data is streaming; rhythm follows the count rate.
        var breath = (Storyboard)AmbientLayer.Resources["Breath"];
        void Breathe()
        {
            breath.Stop();
            if (!ViewModel.PulseActive) return;
            breath.SpeedRatio = ViewModel.PulseSpeed > 0 ? ViewModel.PulseSpeed : 1.0;
            breath.Begin();
        }
        breath.Completed += (_, _) => Breathe();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.PulseActive)) Breathe();
            else if (e.PropertyName == nameof(MainViewModel.ThemeIndex)) ApplyTheme();
            if (e.PropertyName is nameof(MainViewModel.IsPaused) or nameof(MainViewModel.IsConnected)) UpdateConnectionState();
        };
        // Status glow: one pulse per reading.
        var statusPulse = (Storyboard)AppTitleBar.Resources["StatusPulse"];
        ViewModel.ReadingReceived += () =>
        {
            if (!ViewModel.AnimationsEnabled) return;
            statusPulse.Stop();
            statusPulse.Begin();
        };
        _stopStatusPulse = statusPulse.Stop;

        // Close the device link on every way out: window closed, process exit (incl. SIGTERM), unhandled exception.
        if (App.MainWindow is { } window) window.Closed += (_, _) => ViewModel.Shutdown();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ViewModel.Shutdown();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.IsTerminating) ViewModel.Shutdown(); };
        ViewModel.ChooseDevice = ChooseDeviceAsync;

        Loaded += (_, _) =>
        {
            ApplyTheme();
            UpdateConnectionState();
            SetMinimumWindowSize(600, 560);
        };

#if WINDOWS10_0_17763_0_OR_GREATER
        // Let the Mica backdrop show through and use our own header as the title bar.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Loaded += (_, _) => App.MainWindow?.SetTitleBar(AppTitleBar);
        ActualThemeChanged += (_, _) => UpdateCaptionButtons();
#endif
    }

    private Action? _stopStatusPulse;

    private async Task<DeviceChoice?> ChooseDeviceAsync(IReadOnlyList<DeviceChoice> devices)
    {
        var list = new ListView
        {
            ItemsSource = devices.Select(d => d.Label).ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
        };
        var dialog = new ContentDialog
        {
            Title = "Choose a device",
            Content = list,
            PrimaryButtonText = "Connect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedIndex >= 0
            ? devices[list.SelectedIndex]
            : null;
    }

    /// <summary>Status dot and text: green while live, amber when paused, red when not connected.</summary>
    private void UpdateConnectionState()
    {
        string state = !ViewModel.IsConnected ? "Offline" : ViewModel.IsPaused ? "Paused" : "Live";
        VisualStateManager.GoToState(this, state, false);
        if (state != "Live") _stopStatusPulse?.Invoke();
    }

    /// <summary>Keeps the window from being dragged smaller than the two-row bottom bar needs.</summary>
    private void SetMinimumWindowSize(int width, int height)
    {
        try
        {
            if (App.MainWindow?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                double scale = XamlRoot?.RasterizationScale ?? 1.0;
                presenter.PreferredMinimumWidth = (int)(width * scale);
                presenter.PreferredMinimumHeight = (int)(height * scale);
            }
        }
        catch
        {
            // Not supported on this platform/compositor; the layout still adapts.
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);

    private void ShowPage(string name)
    {
        foreach (var (key, page) in _pages)
            page.Visibility = key == name ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in _navButtons)
            button.Style = (Style)Application.Current.Resources[(string)button.Tag == name ? "NavButtonSelected" : "NavButton"];
    }

    private void ApplyTheme()
    {
        var theme = ViewModel.ThemeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // Set on the window root so the backdrop and every page follow it.
        if (App.MainWindow?.Content is FrameworkElement root) root.RequestedTheme = theme; else RequestedTheme = theme;
#if WINDOWS10_0_17763_0_OR_GREATER
        UpdateCaptionButtons();
#endif
    }

#if WINDOWS10_0_17763_0_OR_GREATER
    /// <summary>Caption buttons (min/max/close) are drawn by the system; keep their glyphs readable in the chosen theme.</summary>
    private void UpdateCaptionButtons()
    {
        if (App.MainWindow?.AppWindow.TitleBar is not { } bar) return;
        bool dark = ActualTheme == ElementTheme.Dark;
        bar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        bar.ButtonHoverForegroundColor = bar.ButtonForegroundColor;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }
#endif
}
