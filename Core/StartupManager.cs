using Microsoft.Win32;

namespace WallpaperApp.Core;

/// <summary>"Start with Windows" through the per-user Run key, which needs no admin rights.</summary>
public static class StartupManager
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "WallpaperApp";

    static string Command => $"\"{Environment.ProcessPath}\" --startup";

    /// <summary>True when the Run entry exists and hasn't been switched off in Task Manager's Startup tab.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (run?.GetValue(ValueName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            // Task Manager writes 02 for enabled and 03 for disabled in the first byte.
            return !(approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (enabled) run.SetValue(ValueName, Command);
            else run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // Clear any Task Manager "disabled" flag so the checkbox is what counts.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Points an existing Run entry at this copy of the .exe, in case the folder was moved.</summary>
    public static void RepairPath()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (run?.GetValue(ValueName) is string current && current != Command) run.SetValue(ValueName, Command);
    }
}
