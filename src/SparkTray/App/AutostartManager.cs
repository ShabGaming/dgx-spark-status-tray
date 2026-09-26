using Microsoft.Win32;

namespace SparkTray.App;

/// <summary>
/// "Start with Windows" via the per-user HKCU Run key - the standard, no-elevation-needed
/// mechanism for a login-triggered tray convenience app. Task Scheduler is the right tool
/// when you need elevation or complex trigger conditions; neither applies here.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DgxSparkStatusTray";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }
}
