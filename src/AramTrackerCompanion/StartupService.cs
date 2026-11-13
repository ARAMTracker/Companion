using Microsoft.Win32;

namespace AramTrackerCompanion;

internal sealed class StartupService
{
    public const string MinimizedArgument = "--minimized";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    public StartupService(string valueName = "ARAMTrackerCompanion")
    {
        _valueName = valueName;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(_valueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);

        if (enabled)
        {
            key.SetValue(_valueName, Command());
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }

        Log.Info("Start with Windows changed", ("enabled", enabled));
    }

    public void RefreshPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

        if (key?.GetValue(_valueName) is string current && current != Command())
        {
            key.SetValue(_valueName, Command());
        }
    }

    private static string Command() => $"\"{Environment.ProcessPath}\" {MinimizedArgument}";
}
