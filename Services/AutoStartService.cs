using Microsoft.Win32;

namespace ASD4G.Services;

public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ASD4G";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var value = key?.GetValue(AppName) as string;
        return string.Equals(value, BuildCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public bool SetEnabled(bool enabled, out string? errorMessage)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                key.SetValue(AppName, BuildCommand());
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

    private static string BuildCommand()
    {
        return $"\"{Environment.ProcessPath}\"";
    }
}
