using Microsoft.Win32;

namespace ASD4G.Services;

public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ASD4G";
    public const string BackgroundLaunchArgument = "--background";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var value = key?.GetValue(AppName) as string;
        var command = BuildCommand();
        return !string.IsNullOrWhiteSpace(command) &&
               string.Equals(value, command, StringComparison.OrdinalIgnoreCase);
    }

    public bool SetEnabled(bool enabled, out string? errorMessage)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            var command = BuildCommand();

            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(command))
                {
                    throw new InvalidOperationException("Unable to resolve the current executable path.");
                }

                key.SetValue(AppName, command);
            }
            else
            {
                key.DeleteValue(AppName, false);
            }

            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public void Sync(bool enabled)
    {
        if (IsEnabled() != enabled)
        {
            SetEnabled(enabled, out _);
        }
    }

    public static bool ShouldStartHidden(IEnumerable<string> arguments)
    {
        return arguments.Any(argument =>
            string.Equals(argument, BackgroundLaunchArgument, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCommand()
    {
        return string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? string.Empty
            : $"\"{Environment.ProcessPath}\" {BackgroundLaunchArgument}";
    }
}
