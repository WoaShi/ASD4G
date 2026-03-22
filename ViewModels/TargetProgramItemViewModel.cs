using System.IO;
using System.Windows.Media;
using ASD4G.Helpers;
using ASD4G.Infrastructure;
using ASD4G.Models;

namespace ASD4G.ViewModels;

public sealed class TargetProgramItemViewModel : ObservableObject
{
    private int _targetWidth;
    private int _targetHeight;

    public TargetProgramItemViewModel(string executablePath, int targetWidth, int targetHeight)
    {
        ExecutablePath = executablePath;
        DisplayName = Path.GetFileNameWithoutExtension(executablePath);
        Icon = ExecutableIconHelper.LoadIcon(executablePath);
        _targetWidth = Math.Max(1, targetWidth);
        _targetHeight = Math.Max(1, targetHeight);
    }

    public event EventHandler? ResolutionChanged;

    public string ExecutablePath { get; }

    public string DisplayName { get; }

    public ImageSource? Icon { get; }

    public int TargetWidth
    {
        get => _targetWidth;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (!SetProperty(ref _targetWidth, normalizedValue))
            {
                return;
            }

            OnPropertyChanged(nameof(TargetResolutionText));
            ResolutionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int TargetHeight
    {
        get => _targetHeight;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (!SetProperty(ref _targetHeight, normalizedValue))
            {
                return;
            }

            OnPropertyChanged(nameof(TargetResolutionText));
            ResolutionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string TargetResolutionText => $"{TargetWidth} x {TargetHeight}";

    public TargetProgramSetting ToSetting()
    {
        return new TargetProgramSetting
        {
            ExecutablePath = ExecutablePath,
            TargetWidth = TargetWidth,
            TargetHeight = TargetHeight
        };
    }
}
