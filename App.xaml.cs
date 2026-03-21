using System.IO;
using System.Windows;
using ASD4G.Services;
using ASD4G.ViewModels;

namespace ASD4G;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }
}
