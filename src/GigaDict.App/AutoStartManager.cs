using Microsoft.Win32;

namespace GigaDict.App;

internal static class AutoStartManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled) key.SetValue("GigaDict", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("GigaDict", false);
    }
}

