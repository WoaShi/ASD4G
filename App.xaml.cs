using System.ComponentModel;
using System.IO;
using System.Windows;
using ASD4G.Services;
using ASD4G.ViewModels;
using Application = System.Windows.Application;

namespace ASD4G;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private TrayIconService? _trayIconService;
    private bool _isExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        var localizationService = LocalizationService.Instance;
        localizationService.Initialize(Path.Combine(AppContext.BaseDirectory, "Languages"), settings.SelectedLanguage);

        var autoStartService = new AutoStartService();
        autoStartService.Sync(settings.AutoStart);

        var displayScalingService = new DisplayScalingService();
        var processMonitorService = new ProcessMonitorService();

        _mainViewModel = new MainViewModel(
            settings,
            settingsService,
            localizationService,
            autoStartService,
            displayScalingService,
            processMonitorService);

        var window = new MainWindow
        {
            DataContext = _mainViewModel
        };

        MainWindow = window;
        window.Closing += OnMainWindowClosing;

        _trayIconService = new TrayIconService(localizationService);
        _trayIconService.ShowRequested += OnTrayShowRequested;
        _trayIconService.ExitRequested += OnTrayExitRequested;
        _trayIconService.IconThemeChanged += OnTrayIconThemeChanged;
        ApplyWindowIcon(window);

        if (AutoStartService.ShouldStartHidden(e.Args))
        {
            window.PrepareForBackgroundStart();
        }
        else
        {
            window.ShowFromTray();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _isExitRequested = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIconService is not null)
        {
            _trayIconService.ShowRequested -= OnTrayShowRequested;
            _trayIconService.ExitRequested -= OnTrayExitRequested;
            _trayIconService.IconThemeChanged -= OnTrayIconThemeChanged;
            _trayIconService.Dispose();
        }

        _mainViewModel?.Dispose();
        base.OnExit(e);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;

        if (sender is MainWindow window)
        {
            window.HideToTray();
        }
    }

    private void OnTrayShowRequested(object? sender, EventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.ShowFromTray();
        }
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        _isExitRequested = true;
        Shutdown();
    }

    private void OnTrayIconThemeChanged(object? sender, EventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            ApplyWindowIcon(window);
        }
    }

    private void ApplyWindowIcon(MainWindow window)
    {
        if (_trayIconService is not null)
        {
            window.Icon = _trayIconService.CurrentWindowIcon;
        }
    }
}
