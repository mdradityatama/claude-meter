using Microsoft.Win32;

namespace ClaudeMeter.UI;

/// <summary>"Start with Windows" via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeMeter";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True only if the entry points at this exe (a moved exe shows as disabled).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value
                && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
