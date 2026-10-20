using GigaDict.Core.Configuration;
using Microsoft.Win32;

namespace GigaDict.App;

// The registry is the source of truth: Task Manager can disable the entry without
// GigaDict knowing, and settings.json only records the last choice made here.
internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "GigaDict";

    internal static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Путь к GigaDict.exe неизвестен.");

    internal static bool IsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return AutoStartRules.IsEnabled(run?.GetValue(ValueName) as string, approved?.GetValue(ValueName) as byte[]);
    }

    internal static void SetEnabled(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey, true))
        {
            if (enabled) run.SetValue(ValueName, AutoStartRules.CommandFor(ExecutablePath));
            else run.DeleteValue(ValueName, false);
        }
        // Enabling here overrides an earlier "disabled" from Task Manager; after
        // disabling, the approval record refers to nothing. Remove it either way.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true);
        approved?.DeleteValue(ValueName, false);
    }

    internal static void RefreshPath()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (run is null) return;
        if (AutoStartRules.NeedsPathUpdate(run.GetValue(ValueName) as string, ExecutablePath))
            run.SetValue(ValueName, AutoStartRules.CommandFor(ExecutablePath));
    }
}
