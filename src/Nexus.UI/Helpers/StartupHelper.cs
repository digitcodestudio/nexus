using Microsoft.Win32;

namespace Nexus.UI.Helpers;

public static class StartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "NEXUS_System_Optimizer";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Nexus.UI.exe");
                key.SetValue(AppValueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AppValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}

