using System.IO;
using System.Windows.Media;
using ASD4G.Helpers;

namespace ASD4G.ViewModels;

public sealed class TargetProgramItemViewModel
{
    public TargetProgramItemViewModel(string executablePath)
    {
        ExecutablePath = executablePath;
        DisplayName = Path.GetFileNameWithoutExtension(executablePath);
        Icon = ExecutableIconHelper.LoadIcon(executablePath);
    }

    public string ExecutablePath { get; }

    public string DisplayName { get; }

    public ImageSource? Icon { get; }
}
