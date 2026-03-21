using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ASD4G.Infrastructure;
using ASD4G.Models;
using ASD4G.Services;

namespace ASD4G.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly Brush AccentBrush = CreateBrush("#2563EB");
    private static readonly Brush SuccessBrush = CreateBrush("#15803D");
    private static readonly Brush WarningBrush = CreateBrush("#B45309");
    private static readonly Brush ErrorBrush = CreateBrush("#B42318");

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;
    private readonly AutoStartService _autoStartService;
    private readonly DisplayScalingService _displayScalingService;
    private readonly ProcessMonitorService _processMonitorService;
    private readonly RelayCommand _startMonitoringCommand;
    private readonly RelayCommand _stopMonitoringCommand;
    private readonly RelayCommand _addTargetProgramCommand;
    private readonly RelayCommand _removeTargetProgramCommand;
    private readonly RelayCommand<string> _applyPresetCommand;
    private TargetProgramItemViewModel? _selectedTargetProgram;
    private LanguageOption? _selectedLanguage;
    private bool _autoStartEnabled;
    private bool _isMonitoringEnabled;
    private bool _isOperationPageSelected = true;
    private int _targetWidth;
    private int _targetHeight;
    private string _statusMessageKey = "Status.Ready";
    private object[] _statusMessageArgs = [];

    public MainViewModel(
        AppSettings settings,
        SettingsService settingsService,
        LocalizationService localizationService,
        AutoStartService autoStartService,
        DisplayScalingService displayScalingService,
        ProcessMonitorService processMonitorService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _autoStartService = autoStartService;
        _displayScalingService = displayScalingService;
        _processMonitorService = processMonitorService;

        TargetPrograms = [];
        _startMonitoringCommand = new RelayCommand(StartMonitoring, () => !_isMonitoringEnabled);
        _stopMonitoringCommand = new RelayCommand(StopMonitoringAndRestore, () => _isMonitoringEnabled || _displayScalingService.IsScalingApplied);
        _addTargetProgramCommand = new RelayCommand(AddTargetProgram);
        _removeTargetProgramCommand = new RelayCommand(RemoveSelectedTargetProgram, () => SelectedTargetProgram is not null);
        _applyPresetCommand = new RelayCommand<string>(ApplyPreset);

        foreach (var executablePath in settings.TargetPrograms
                     .Where(File.Exists)
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TargetPrograms.Add(new TargetProgramItemViewModel(executablePath));
        }

        _autoStartEnabled = settings.AutoStart;
        _isMonitoringEnabled = settings.MonitoringEnabled;
        _targetWidth = settings.TargetWidth;
        _targetHeight = settings.TargetHeight;
        _selectedLanguage = _localizationService.AvailableLanguages
            .FirstOrDefault(option => string.Equals(option.Code, settings.SelectedLanguage, StringComparison.OrdinalIgnoreCase))
            ?? _localizationService.AvailableLanguages.FirstOrDefault();

        _processMonitorService.ActiveProcessesChanged += OnActiveProcessesChanged;
        _processMonitorService.UpdateTargets(TargetPrograms.Select(item => item.ExecutablePath));

        if (_isMonitoringEnabled)
        {
            _processMonitorService.Start();
            SetStatusMessage("Status.WaitingForProcess");
        }
        else
        {
            SetStatusMessage("Status.MonitoringPaused");
        }

        _localizationService.LanguageChanged += OnLanguageChanged;
        RefreshState();
    }

    public ObservableCollection<TargetProgramItemViewModel> TargetPrograms { get; }

    public IEnumerable<LanguageOption> AvailableLanguages => _localizationService.AvailableLanguages;

    public ICommand StartMonitoringCommand => _startMonitoringCommand;

    public ICommand StopMonitoringCommand => _stopMonitoringCommand;

    public ICommand AddTargetProgramCommand => _addTargetProgramCommand;

    public ICommand RemoveTargetProgramCommand => _removeTargetProgramCommand;

    public ICommand ApplyPresetCommand => _applyPresetCommand;

    public TargetProgramItemViewModel? SelectedTargetProgram
    {
        get => _selectedTargetProgram;
        set
        {
            if (SetProperty(ref _selectedTargetProgram, value))
            {
                OnPropertyChanged(nameof(HasSelectedTargetProgram));
                _removeTargetProgramCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            if (_localizationService.SetLanguage(value.Code))
            {
                _settings.SelectedLanguage = value.Code;
                SaveSettings();
            }
        }
    }

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set
        {
            if (!SetProperty(ref _autoStartEnabled, value))
            {
                return;
            }

            if (_autoStartService.SetEnabled(value, out var errorMessage))
            {
                _settings.AutoStart = value;
                SaveSettings();
                SetStatusMessage(value ? "Status.AutoStartEnabled" : "Status.AutoStartDisabled");
            }
            else
            {
                _autoStartEnabled = !value;
                OnPropertyChanged();
                SetStatusMessage("Status.AutoStartFailed", errorMessage ?? string.Empty);
            }

            RefreshState();
        }
    }

    public bool IsMonitoringEnabled => _isMonitoringEnabled;

    public bool IsOperationPageSelected
    {
        get => _isOperationPageSelected;
        set
        {
            if (SetProperty(ref _isOperationPageSelected, value))
            {
                OnPropertyChanged(nameof(IsSettingsPageSelected));
            }
        }
    }

    public bool IsSettingsPageSelected
    {
        get => !IsOperationPageSelected;
        set
        {
            if (value)
            {
                IsOperationPageSelected = false;
            }
        }
    }

    public int TargetWidth
    {
        get => _targetWidth;
        set
        {
            if (SetProperty(ref _targetWidth, Math.Max(1, value)))
            {
                _settings.TargetWidth = _targetWidth;
                SaveSettings();
                RefreshState();
                ReapplyScalingIfNeeded();
            }
        }
    }

    public int TargetHeight
    {
        get => _targetHeight;
        set
        {
            if (SetProperty(ref _targetHeight, Math.Max(1, value)))
            {
                _settings.TargetHeight = _targetHeight;
                SaveSettings();
                RefreshState();
                ReapplyScalingIfNeeded();
            }
        }
    }

    public bool HasSelectedTargetProgram => SelectedTargetProgram is not null;

    public string MonitorStateText => Translate(_isMonitoringEnabled ? "Status.MonitoringEnabled" : "Status.MonitoringDisabled");

    public string ScaleStateText => Translate(_displayScalingService.IsScalingApplied ? "Status.ScalingApplied" : "Status.ScalingIdle");

    public string TargetResolutionText => $"{TargetWidth} x {TargetHeight}";

    public string CurrentResolutionText => FormatDisplayMode(_displayScalingService.GetCurrentMode());

    public string OriginalResolutionText => FormatDisplayMode(_displayScalingService.OriginalMode);

    public string ActiveProcessesText => _processMonitorService.ActiveProcesses.Count == 0
        ? Translate("Status.NoActiveProcess")
        : string.Join(Environment.NewLine, _processMonitorService.ActiveProcesses.Select(process => $"{process.ProcessName} ({process.ProcessId})"));

    public string TargetProgramsCountText => _localizationService.TranslateFormat("Status.TargetProgramsCount", TargetPrograms.Count);

    public string TargetProgramsCountValueText => TargetPrograms.Count.ToString();

    public string AutoStartStateText => Translate(_autoStartEnabled ? "Overview.On" : "Overview.Off");

    public string SelectedLanguageText => SelectedLanguage?.DisplayName ?? Translate("Status.Unknown");

    public string ResolutionAvailabilityText => Translate(_displayScalingService.IsTargetModeSupported(TargetWidth, TargetHeight)
        ? "Status.TargetResolutionSupported"
        : "Status.TargetResolutionUnsupported");

    public string StatusMessage => _localizationService.TranslateFormat(_statusMessageKey, _statusMessageArgs);

    public Brush MonitorStateBrush => _isMonitoringEnabled ? AccentBrush : ErrorBrush;

    public Brush ScaleStateBrush => _displayScalingService.IsScalingApplied ? SuccessBrush : WarningBrush;

    public Brush ResolutionAvailabilityBrush => _displayScalingService.IsTargetModeSupported(TargetWidth, TargetHeight)
        ? SuccessBrush
        : WarningBrush;

    public void Dispose()
    {
        _processMonitorService.ActiveProcessesChanged -= OnActiveProcessesChanged;
        _processMonitorService.Dispose();
        _localizationService.LanguageChanged -= OnLanguageChanged;

        if (_displayScalingService.IsScalingApplied)
        {
            _displayScalingService.RestoreResolution();
        }
    }

    private void StartMonitoring()
    {
        if (_isMonitoringEnabled)
        {
            return;
        }

        _isMonitoringEnabled = true;
        _settings.MonitoringEnabled = true;
        SaveSettings();
        _processMonitorService.Start();
        SetStatusMessage("Status.WaitingForProcess");
        RefreshState();
    }

    private void StopMonitoringAndRestore()
    {
        if (!_isMonitoringEnabled && !_displayScalingService.IsScalingApplied)
        {
            return;
        }

        _isMonitoringEnabled = false;
        _settings.MonitoringEnabled = false;
        SaveSettings();
        _processMonitorService.Stop();

        if (_displayScalingService.IsScalingApplied)
        {
            var result = _displayScalingService.RestoreResolution();
            SetStatusMessage(result.Success ? "Status.RestoredToOriginal" : "Status.RestoreFailed");
        }
        else
        {
            SetStatusMessage("Status.MonitoringPaused");
        }

        RefreshState();
    }

    private void AddTargetProgram()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(dialog.FileName);
        if (TargetPrograms.Any(item => string.Equals(item.ExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatusMessage("Status.TargetProgramExists");
            return;
        }

        var item = new TargetProgramItemViewModel(normalizedPath);
        TargetPrograms.Add(item);
        SelectedTargetProgram = item;

        _settings.TargetPrograms = TargetPrograms.Select(target => target.ExecutablePath).ToList();
        SaveSettings();
        _processMonitorService.UpdateTargets(TargetPrograms.Select(target => target.ExecutablePath));

        SetStatusMessage("Status.TargetProgramAdded", Path.GetFileName(normalizedPath));
        RefreshState();
    }

    private void RemoveSelectedTargetProgram()
    {
        if (SelectedTargetProgram is null)
        {
            return;
        }

        var removedName = Path.GetFileName(SelectedTargetProgram.ExecutablePath);
        TargetPrograms.Remove(SelectedTargetProgram);
        SelectedTargetProgram = null;

        _settings.TargetPrograms = TargetPrograms.Select(target => target.ExecutablePath).ToList();
        SaveSettings();
        _processMonitorService.UpdateTargets(TargetPrograms.Select(target => target.ExecutablePath));

        SetStatusMessage("Status.TargetProgramRemoved", removedName);
        RefreshState();
    }

    private void ApplyPreset(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var parts = payload.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height))
        {
            return;
        }

        _targetWidth = width;
        _targetHeight = height;
        _settings.TargetWidth = width;
        _settings.TargetHeight = height;
        SaveSettings();

        OnPropertyChanged(nameof(TargetWidth));
        OnPropertyChanged(nameof(TargetHeight));
        SetStatusMessage("Status.PresetApplied", width, height);
        RefreshState();
        ReapplyScalingIfNeeded();
    }

    private void ReapplyScalingIfNeeded()
    {
        if (!_displayScalingService.IsScalingApplied)
        {
            return;
        }

        var result = _displayScalingService.ApplyResolution(TargetWidth, TargetHeight);
        if (!result.Success)
        {
            SetStatusMessage(MapDisplayFailure(result.Failure), TargetWidth, TargetHeight);
        }

        RefreshState();
    }

    private void OnActiveProcessesChanged(object? sender, EventArgs e)
    {
        if (!_isMonitoringEnabled)
        {
            RefreshState();
            return;
        }

        if (_processMonitorService.ActiveProcesses.Count > 0)
        {
            if (!_displayScalingService.IsScalingApplied)
            {
                var result = _displayScalingService.ApplyResolution(TargetWidth, TargetHeight);
                SetStatusMessage(
                    result.Success ? "Status.ScaleApplied" : MapDisplayFailure(result.Failure),
                    TargetWidth,
                    TargetHeight);
            }
            else
            {
                SetStatusMessage("Status.ProcessDetected");
            }
        }
        else if (_displayScalingService.IsScalingApplied)
        {
            var result = _displayScalingService.RestoreResolution();
            SetStatusMessage(result.Success ? "Status.RestoredAfterExit" : "Status.RestoreFailed");
        }
        else
        {
            SetStatusMessage("Status.WaitingForProcess");
        }

        RefreshState();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshState();
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsMonitoringEnabled));
        OnPropertyChanged(nameof(IsSettingsPageSelected));
        OnPropertyChanged(nameof(MonitorStateText));
        OnPropertyChanged(nameof(ScaleStateText));
        OnPropertyChanged(nameof(TargetResolutionText));
        OnPropertyChanged(nameof(CurrentResolutionText));
        OnPropertyChanged(nameof(OriginalResolutionText));
        OnPropertyChanged(nameof(ActiveProcessesText));
        OnPropertyChanged(nameof(TargetProgramsCountText));
        OnPropertyChanged(nameof(TargetProgramsCountValueText));
        OnPropertyChanged(nameof(AutoStartStateText));
        OnPropertyChanged(nameof(SelectedLanguageText));
        OnPropertyChanged(nameof(ResolutionAvailabilityText));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(MonitorStateBrush));
        OnPropertyChanged(nameof(ScaleStateBrush));
        OnPropertyChanged(nameof(ResolutionAvailabilityBrush));
        _startMonitoringCommand.RaiseCanExecuteChanged();
        _stopMonitoringCommand.RaiseCanExecuteChanged();
        _removeTargetProgramCommand.RaiseCanExecuteChanged();
    }

    private void SetStatusMessage(string key, params object[] args)
    {
        _statusMessageKey = key;
        _statusMessageArgs = args;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private string Translate(string key)
    {
        return _localizationService.Translate(key);
    }

    private string MapDisplayFailure(DisplayOperationFailure failure)
    {
        return failure switch
        {
            DisplayOperationFailure.CurrentModeUnavailable => "Status.CurrentModeUnavailable",
            DisplayOperationFailure.TargetModeUnsupported => "Status.TargetResolutionUnsupported",
            DisplayOperationFailure.ApplyFailed => "Status.ApplyFailed",
            DisplayOperationFailure.RestoreFailed => "Status.RestoreFailed",
            _ => "Status.ApplyFailed"
        };
    }

    private string FormatDisplayMode(DisplayMode? mode)
    {
        return mode is null
            ? Translate("Status.Unknown")
            : $"{mode.Width} x {mode.Height} @ {mode.DisplayFrequency}Hz";
    }

    private static Brush CreateBrush(string colorCode)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(colorCode)!;
        brush.Freeze();
        return brush;
    }
}
