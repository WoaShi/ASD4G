using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using ASD4G.Infrastructure;
using ASD4G.Models;
using ASD4G.Services;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

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
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _processStateGate = new(1, 1);
    private readonly SemaphoreSlim _displaySnapshotGate = new(1, 1);
    private readonly RelayCommand _startMonitoringCommand;
    private readonly RelayCommand _stopMonitoringCommand;
    private readonly RelayCommand _applyManualScalingCommand;
    private readonly RelayCommand _disableManualScalingCommand;
    private readonly RelayCommand _addTargetProgramCommand;
    private readonly RelayCommand _removeTargetProgramCommand;
    private readonly RelayCommand<string> _applyPresetCommand;
    private TargetProgramItemViewModel? _selectedTargetProgram;
    private LanguageOption? _selectedLanguage;
    private bool _autoStartEnabled;
    private bool _isMonitoringEnabled;
    private bool _isOperationPageSelected = true;
    private bool _isDisposed;
    private bool _isManualScalingOverrideActive;
    private bool _suppressTargetProgramResolutionChanged;
    private int _manualOverrideWidth;
    private int _manualOverrideHeight;
    private int _defaultTargetWidth;
    private int _defaultTargetHeight;
    private string _statusMessageKey = "Status.Ready";
    private object[] _statusMessageArgs = [];
    private string _currentResolutionText = string.Empty;
    private string _originalResolutionText = string.Empty;
    private string _resolutionAvailabilityText = string.Empty;
    private Brush _resolutionAvailabilityBrush = WarningBrush;

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
        _dispatcher = Dispatcher.CurrentDispatcher;

        TargetPrograms = [];
        _startMonitoringCommand = new RelayCommand(StartMonitoring, () => !_isMonitoringEnabled);
        _stopMonitoringCommand = new RelayCommand(StopMonitoringAndRestore, () => _isMonitoringEnabled || _displayScalingService.IsScalingApplied);
        _applyManualScalingCommand = new RelayCommand(ApplyManualScaling, () => !_isDisposed);
        _disableManualScalingCommand = new RelayCommand(DisableManualScaling, () => !_isDisposed && (_displayScalingService.IsScalingApplied || _isManualScalingOverrideActive));
        _addTargetProgramCommand = new RelayCommand(AddTargetProgram);
        _removeTargetProgramCommand = new RelayCommand(RemoveSelectedTargetProgram, () => SelectedTargetProgram is not null);
        _applyPresetCommand = new RelayCommand<string>(ApplyPreset);

        _autoStartEnabled = settings.AutoStart;
        _isMonitoringEnabled = settings.MonitoringEnabled;
        _defaultTargetWidth = Math.Max(1, settings.TargetWidth);
        _defaultTargetHeight = Math.Max(1, settings.TargetHeight);
        _selectedLanguage = _localizationService.AvailableLanguages
            .FirstOrDefault(option => string.Equals(option.Code, settings.SelectedLanguage, StringComparison.OrdinalIgnoreCase))
            ?? _localizationService.AvailableLanguages.FirstOrDefault();

        foreach (var configuredProgram in LoadConfiguredPrograms(settings))
        {
            var item = new TargetProgramItemViewModel(
                configuredProgram.ExecutablePath,
                configuredProgram.TargetWidth,
                configuredProgram.TargetHeight);
            item.ResolutionChanged += OnTargetProgramResolutionChanged;
            TargetPrograms.Add(item);
        }

        if (TargetPrograms.Count > 0)
        {
            _selectedTargetProgram = TargetPrograms[0];
        }

        _processMonitorService.ActiveProcessesChanged += OnActiveProcessesChanged;
        _processMonitorService.UpdateTargets(BuildTargetProgramSettingsSnapshot());

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
        _ = RefreshDisplaySnapshotAsync();
        _ = HandleActiveProcessesChangedAsync();
    }

    public ObservableCollection<TargetProgramItemViewModel> TargetPrograms { get; }

    public IEnumerable<LanguageOption> AvailableLanguages => _localizationService.AvailableLanguages;

    public RelayCommand StartMonitoringCommand => _startMonitoringCommand;

    public RelayCommand StopMonitoringCommand => _stopMonitoringCommand;

    public RelayCommand AddTargetProgramCommand => _addTargetProgramCommand;

    public RelayCommand RemoveTargetProgramCommand => _removeTargetProgramCommand;

    public RelayCommand ApplyManualScalingCommand => _applyManualScalingCommand;

    public RelayCommand DisableManualScalingCommand => _disableManualScalingCommand;

    public RelayCommand<string> ApplyPresetCommand => _applyPresetCommand;

    public TargetProgramItemViewModel? SelectedTargetProgram
    {
        get => _selectedTargetProgram;
        set
        {
            if (!SetProperty(ref _selectedTargetProgram, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedTargetProgram));
            OnPropertyChanged(nameof(TargetWidth));
            OnPropertyChanged(nameof(TargetHeight));
            OnPropertyChanged(nameof(TargetResolutionText));
            OnPropertyChanged(nameof(ResolutionEditorScopeText));
            _removeTargetProgramCommand.RaiseCanExecuteChanged();
            RefreshState();
            _ = RefreshDisplaySnapshotAsync();
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
        get => SelectedTargetProgram?.TargetWidth ?? _defaultTargetWidth;
        set => UpdateResolutionConfiguration(Math.Max(1, value), TargetHeight);
    }

    public int TargetHeight
    {
        get => SelectedTargetProgram?.TargetHeight ?? _defaultTargetHeight;
        set => UpdateResolutionConfiguration(TargetWidth, Math.Max(1, value));
    }

    public bool HasSelectedTargetProgram => SelectedTargetProgram is not null;

    public string ResolutionEditorScopeText => SelectedTargetProgram is null
        ? Translate("Settings.DefaultResolutionScope")
        : _localizationService.TranslateFormat("Settings.SelectedProgramResolutionScope", SelectedTargetProgram.DisplayName);

    public string MonitorStateText => Translate(_isMonitoringEnabled ? "Status.MonitoringEnabled" : "Status.MonitoringDisabled");

    public string ScaleStateText => Translate(_displayScalingService.IsScalingApplied ? "Status.ScalingApplied" : "Status.ScalingIdle");

    public string TargetResolutionText
    {
        get
        {
            var targetResolution = GetDisplayedTargetResolution();
            return $"{targetResolution.Width} x {targetResolution.Height}";
        }
    }

    public string CurrentResolutionText => _currentResolutionText;

    public string OriginalResolutionText => _originalResolutionText;

    public string ActiveProcessesText => _processMonitorService.ActiveProcesses.Count == 0
        ? Translate("Status.NoActiveProcess")
        : string.Join(
            Environment.NewLine,
            _processMonitorService.ActiveProcesses.Select(process =>
                $"{process.ProcessName} ({process.ProcessId})  [{process.TargetWidth} x {process.TargetHeight}]"));

    public string TargetProgramsCountText => _localizationService.TranslateFormat("Status.TargetProgramsCount", TargetPrograms.Count);

    public string TargetProgramsCountValueText => TargetPrograms.Count.ToString();

    public string AutoStartStateText => Translate(_autoStartEnabled ? "Overview.On" : "Overview.Off");

    public string SelectedLanguageText => SelectedLanguage?.DisplayName ?? Translate("Status.Unknown");

    public string ResolutionAvailabilityText => _resolutionAvailabilityText;

    public string StatusMessage => _localizationService.TranslateFormat(_statusMessageKey, _statusMessageArgs);

    public Brush MonitorStateBrush => _isMonitoringEnabled ? AccentBrush : ErrorBrush;

    public Brush ScaleStateBrush => _displayScalingService.IsScalingApplied ? SuccessBrush : WarningBrush;

    public Brush ResolutionAvailabilityBrush => _resolutionAvailabilityBrush;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isManualScalingOverrideActive = false;
        _processMonitorService.ActiveProcessesChanged -= OnActiveProcessesChanged;
        _processMonitorService.Dispose();
        _localizationService.LanguageChanged -= OnLanguageChanged;

        foreach (var targetProgram in TargetPrograms)
        {
            targetProgram.ResolutionChanged -= OnTargetProgramResolutionChanged;
        }

        if (_displayScalingService.IsScalingApplied)
        {
            _displayScalingService.RestoreResolution();
        }

        _processStateGate.Dispose();
        _displaySnapshotGate.Dispose();
    }

    private static IEnumerable<TargetProgramSetting> LoadConfiguredPrograms(AppSettings settings)
    {
        if (settings.TargetProgramSettings.Count > 0)
        {
            return settings.TargetProgramSettings
                .Where(program => !string.IsNullOrWhiteSpace(program.ExecutablePath))
                .Select(program => new TargetProgramSetting
                {
                    ExecutablePath = Path.GetFullPath(program.ExecutablePath),
                    TargetWidth = Math.Max(1, program.TargetWidth),
                    TargetHeight = Math.Max(1, program.TargetHeight)
                })
                .Where(program => File.Exists(program.ExecutablePath))
                .DistinctBy(program => program.ExecutablePath, StringComparer.OrdinalIgnoreCase);
        }

        return settings.TargetPrograms
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new TargetProgramSetting
            {
                ExecutablePath = path,
                TargetWidth = Math.Max(1, settings.TargetWidth),
                TargetHeight = Math.Max(1, settings.TargetHeight)
            });
    }

    private void StartMonitoring()
    {
        _ = StartMonitoringAsync();
    }

    private async Task StartMonitoringAsync()
    {
        if (_isMonitoringEnabled || _isDisposed)
        {
            return;
        }

        _isMonitoringEnabled = true;
        _settings.MonitoringEnabled = true;
        SaveSettings();
        _processMonitorService.Start();

        await _dispatcher.InvokeAsync(() =>
        {
            SetStatusMessage("Status.WaitingForProcess");
            RefreshState();
        });

        await HandleActiveProcessesChangedAsync();
    }

    private void StopMonitoringAndRestore()
    {
        _ = StopMonitoringAndRestoreAsync();
    }

    private async Task StopMonitoringAndRestoreAsync()
    {
        if ((!_isMonitoringEnabled && !_displayScalingService.IsScalingApplied) || _isDisposed)
        {
            return;
        }

        _isMonitoringEnabled = false;
        _isManualScalingOverrideActive = false;
        _settings.MonitoringEnabled = false;
        SaveSettings();
        _processMonitorService.Stop();

        var statusKey = "Status.MonitoringPaused";
        if (_displayScalingService.IsScalingApplied)
        {
            var result = await Task.Run(_displayScalingService.RestoreResolution);
            statusKey = result.Success ? "Status.RestoredToOriginal" : "Status.RestoreFailed";
        }

        await _dispatcher.InvokeAsync(() =>
        {
            SetStatusMessage(statusKey);
            RefreshState();
        });

        await RefreshDisplaySnapshotAsync();
    }

    private void ApplyManualScaling()
    {
        _ = ApplyManualScalingAsync();
    }

    private async Task ApplyManualScalingAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        var targetWidth = TargetWidth;
        var targetHeight = TargetHeight;
        var previousOverrideEnabled = _isManualScalingOverrideActive;
        var previousOverrideWidth = _manualOverrideWidth;
        var previousOverrideHeight = _manualOverrideHeight;

        var result = await Task.Run(() => _displayScalingService.ApplyResolution(targetWidth, targetHeight));
        if (result.Success)
        {
            _isManualScalingOverrideActive = true;
            _manualOverrideWidth = targetWidth;
            _manualOverrideHeight = targetHeight;
        }
        else
        {
            _isManualScalingOverrideActive = previousOverrideEnabled;
            _manualOverrideWidth = previousOverrideWidth;
            _manualOverrideHeight = previousOverrideHeight;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            SetStatusMessage(
                result.Success ? "Status.ManualScaleApplied" : MapDisplayFailure(result.Failure),
                targetWidth,
                targetHeight);
            RefreshState();
        });

        await RefreshDisplaySnapshotAsync();
    }

    private void DisableManualScaling()
    {
        _ = DisableManualScalingAsync();
    }

    private async Task DisableManualScalingAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isManualScalingOverrideActive = false;
        _manualOverrideWidth = 0;
        _manualOverrideHeight = 0;

        string statusKey;
        if (_displayScalingService.IsScalingApplied)
        {
            var result = await Task.Run(_displayScalingService.RestoreResolution);
            statusKey = result.Success ? "Status.ManualScaleClosed" : "Status.RestoreFailed";
        }
        else
        {
            statusKey = _isMonitoringEnabled ? "Status.WaitingForProcess" : "Status.MonitoringPaused";
        }

        await _dispatcher.InvokeAsync(() =>
        {
            SetStatusMessage(statusKey);
            RefreshState();
        });

        await RefreshDisplaySnapshotAsync();
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
            RefreshState();
            return;
        }

        var item = new TargetProgramItemViewModel(normalizedPath, _defaultTargetWidth, _defaultTargetHeight);
        item.ResolutionChanged += OnTargetProgramResolutionChanged;
        TargetPrograms.Add(item);
        SelectedTargetProgram = item;

        PersistTargetPrograms();
        _processMonitorService.UpdateTargets(BuildTargetProgramSettingsSnapshot());

        SetStatusMessage("Status.TargetProgramAdded", Path.GetFileName(normalizedPath));
        RefreshState();
        _ = RefreshDisplaySnapshotAsync();
    }

    private void RemoveSelectedTargetProgram()
    {
        if (SelectedTargetProgram is null)
        {
            return;
        }

        var removedProgram = SelectedTargetProgram;
        var removedName = Path.GetFileName(removedProgram.ExecutablePath);
        removedProgram.ResolutionChanged -= OnTargetProgramResolutionChanged;

        var removedIndex = TargetPrograms.IndexOf(removedProgram);
        TargetPrograms.Remove(removedProgram);

        SelectedTargetProgram = TargetPrograms.Count == 0
            ? null
            : TargetPrograms[Math.Clamp(removedIndex, 0, TargetPrograms.Count - 1)];

        PersistTargetPrograms();
        _processMonitorService.UpdateTargets(BuildTargetProgramSettingsSnapshot());

        SetStatusMessage("Status.TargetProgramRemoved", removedName);
        RefreshState();
        _ = RefreshDisplaySnapshotAsync();
        _ = HandleActiveProcessesChangedAsync();
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

        UpdateResolutionConfiguration(width, height);
        SetStatusMessage("Status.PresetApplied", width, height);
        RefreshState();
    }

    private void UpdateResolutionConfiguration(int width, int height)
    {
        if (SelectedTargetProgram is not null)
        {
            _suppressTargetProgramResolutionChanged = true;
            try
            {
                SelectedTargetProgram.TargetWidth = width;
                SelectedTargetProgram.TargetHeight = height;
            }
            finally
            {
                _suppressTargetProgramResolutionChanged = false;
            }

            _defaultTargetWidth = width;
            _defaultTargetHeight = height;
            _settings.TargetWidth = width;
            _settings.TargetHeight = height;
            PersistTargetPrograms();
        }
        else
        {
            if (_defaultTargetWidth == width && _defaultTargetHeight == height)
            {
                return;
            }

            _defaultTargetWidth = width;
            _defaultTargetHeight = height;
            _settings.TargetWidth = width;
            _settings.TargetHeight = height;
            SaveSettings();
        }

        OnPropertyChanged(nameof(TargetWidth));
        OnPropertyChanged(nameof(TargetHeight));
        OnPropertyChanged(nameof(TargetResolutionText));
        _ = RefreshDisplaySnapshotAsync();
        _ = HandleActiveProcessesChangedAsync();
    }

    private async void OnActiveProcessesChanged(object? sender, EventArgs e)
    {
        await HandleActiveProcessesChangedAsync();
    }

    private async Task HandleActiveProcessesChangedAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _processStateGate.WaitAsync();
        try
        {
            if (!_isMonitoringEnabled)
            {
                if (_isManualScalingOverrideActive)
                {
                    await EnsureManualOverrideAppliedAsync();
                }

                await _dispatcher.InvokeAsync(RefreshState);
                await RefreshDisplaySnapshotAsync();
                return;
            }

            var activeProcesses = _processMonitorService.ActiveProcesses;
            var activeTarget = activeProcesses
                .OrderBy(process => process.TargetPriority)
                .ThenBy(process => process.ProcessId)
                .FirstOrDefault();

            string statusKey;
            object[] statusArgs = [];

            if (activeTarget is not null)
            {
                var requiresReapply = !_displayScalingService.IsScalingApplied ||
                                      !IsAppliedResolution(activeTarget.TargetWidth, activeTarget.TargetHeight);

                if (requiresReapply)
                {
                    var result = await Task.Run(() => _displayScalingService.ApplyResolution(activeTarget.TargetWidth, activeTarget.TargetHeight));
                    statusKey = result.Success ? "Status.ScaleApplied" : MapDisplayFailure(result.Failure);
                    statusArgs = [activeTarget.TargetWidth, activeTarget.TargetHeight];
                }
                else
                {
                    statusKey = "Status.ProcessDetected";
                }
            }
            else if (_isManualScalingOverrideActive)
            {
                await EnsureManualOverrideAppliedAsync();
                statusKey = "Status.ManualScaleApplied";
                statusArgs = [_manualOverrideWidth, _manualOverrideHeight];
            }
            else if (_displayScalingService.IsScalingApplied)
            {
                var result = await Task.Run(_displayScalingService.RestoreResolution);
                statusKey = result.Success ? "Status.RestoredAfterExit" : "Status.RestoreFailed";
            }
            else
            {
                statusKey = "Status.WaitingForProcess";
            }

            await _dispatcher.InvokeAsync(() =>
            {
                SetStatusMessage(statusKey, statusArgs);
                RefreshState();
            });

            await RefreshDisplaySnapshotAsync();
        }
        finally
        {
            _processStateGate.Release();
        }
    }

    private async Task EnsureManualOverrideAppliedAsync()
    {
        if (!_isManualScalingOverrideActive || _manualOverrideWidth <= 0 || _manualOverrideHeight <= 0)
        {
            return;
        }

        if (_displayScalingService.IsScalingApplied && IsAppliedResolution(_manualOverrideWidth, _manualOverrideHeight))
        {
            return;
        }

        await Task.Run(() => _displayScalingService.ApplyResolution(_manualOverrideWidth, _manualOverrideHeight));
    }

    private void OnTargetProgramResolutionChanged(object? sender, EventArgs e)
    {
        if (_suppressTargetProgramResolutionChanged || sender is not TargetProgramItemViewModel targetProgram)
        {
            return;
        }

        _defaultTargetWidth = targetProgram.TargetWidth;
        _defaultTargetHeight = targetProgram.TargetHeight;
        _settings.TargetWidth = targetProgram.TargetWidth;
        _settings.TargetHeight = targetProgram.TargetHeight;
        PersistTargetPrograms();

        if (ReferenceEquals(targetProgram, SelectedTargetProgram))
        {
            OnPropertyChanged(nameof(TargetWidth));
            OnPropertyChanged(nameof(TargetHeight));
            OnPropertyChanged(nameof(TargetResolutionText));
        }

        RefreshState();
        _ = RefreshDisplaySnapshotAsync();
        _ = HandleActiveProcessesChangedAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshState();
        _ = RefreshDisplaySnapshotAsync();
    }

    private void PersistTargetPrograms()
    {
        _settings.TargetProgramSettings = BuildTargetProgramSettingsSnapshot();
        _settings.TargetPrograms = _settings.TargetProgramSettings
            .Select(target => target.ExecutablePath)
            .ToList();
        SaveSettings();
    }

    private List<TargetProgramSetting> BuildTargetProgramSettingsSnapshot()
    {
        return TargetPrograms
            .Select(target => target.ToSetting())
            .ToList();
    }

    private async Task RefreshDisplaySnapshotAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _displaySnapshotGate.WaitAsync();
        try
        {
            var targetResolution = GetDisplayedTargetResolution();
            var snapshot = await Task.Run(() => new DisplaySnapshot
            {
                CurrentMode = _displayScalingService.GetCurrentMode(),
                OriginalMode = _displayScalingService.OriginalMode,
                IsSupported = _displayScalingService.IsTargetModeSupported(targetResolution.Width, targetResolution.Height)
            });

            await _dispatcher.InvokeAsync(() =>
            {
                _currentResolutionText = FormatDisplayMode(snapshot.CurrentMode);
                _originalResolutionText = FormatDisplayMode(snapshot.OriginalMode);
                _resolutionAvailabilityText = Translate(snapshot.IsSupported
                    ? "Status.TargetResolutionSupported"
                    : "Status.TargetResolutionUnsupported");
                _resolutionAvailabilityBrush = snapshot.IsSupported ? SuccessBrush : WarningBrush;

                OnPropertyChanged(nameof(CurrentResolutionText));
                OnPropertyChanged(nameof(OriginalResolutionText));
                OnPropertyChanged(nameof(ResolutionAvailabilityText));
                OnPropertyChanged(nameof(ResolutionAvailabilityBrush));
                OnPropertyChanged(nameof(TargetResolutionText));
            });
        }
        finally
        {
            _displaySnapshotGate.Release();
        }
    }

    private (int Width, int Height) GetDisplayedTargetResolution()
    {
        var activeTarget = _processMonitorService.ActiveProcesses
            .OrderBy(process => process.TargetPriority)
            .ThenBy(process => process.ProcessId)
            .FirstOrDefault();
        if (activeTarget is not null)
        {
            return (activeTarget.TargetWidth, activeTarget.TargetHeight);
        }

        if (_isManualScalingOverrideActive && _manualOverrideWidth > 0 && _manualOverrideHeight > 0)
        {
            return (_manualOverrideWidth, _manualOverrideHeight);
        }

        if (SelectedTargetProgram is not null)
        {
            return (SelectedTargetProgram.TargetWidth, SelectedTargetProgram.TargetHeight);
        }

        if (TargetPrograms.Count > 0)
        {
            return (TargetPrograms[0].TargetWidth, TargetPrograms[0].TargetHeight);
        }

        return (_defaultTargetWidth, _defaultTargetHeight);
    }

    private bool IsAppliedResolution(int width, int height)
    {
        var appliedMode = _displayScalingService.AppliedMode;
        return appliedMode is not null &&
               appliedMode.Width == width &&
               appliedMode.Height == height;
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
        OnPropertyChanged(nameof(ResolutionEditorScopeText));
        _startMonitoringCommand.RaiseCanExecuteChanged();
        _stopMonitoringCommand.RaiseCanExecuteChanged();
        _applyManualScalingCommand.RaiseCanExecuteChanged();
        _disableManualScalingCommand.RaiseCanExecuteChanged();
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

    private sealed class DisplaySnapshot
    {
        public DisplayMode? CurrentMode { get; init; }

        public DisplayMode? OriginalMode { get; init; }

        public bool IsSupported { get; init; }
    }
}
