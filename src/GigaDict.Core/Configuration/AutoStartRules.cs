namespace GigaDict.Core.Configuration;

// Pure rules behind the HKCU Run entry; the registry access lives in the app.
public static class AutoStartRules
{
    public static string CommandFor(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\"";
    }

    // Explorer's StartupApproved\Run record: absent means enabled; otherwise an odd
    // first byte marks an entry the user disabled in Task Manager or Settings.
    public static bool IsApproved(byte[]? approved) => approved is not { Length: > 0 } || (approved[0] & 1) == 0;

    public static bool IsEnabled(string? registeredCommand, byte[]? approved) =>
        !string.IsNullOrWhiteSpace(registeredCommand) && IsApproved(approved);

    // The registered path breaks when the EXE is moved; only an existing entry is refreshed.
    public static bool NeedsPathUpdate(string? registeredCommand, string executablePath) =>
        !string.IsNullOrWhiteSpace(registeredCommand) &&
        !string.Equals(registeredCommand.Trim(), CommandFor(executablePath), StringComparison.OrdinalIgnoreCase);

    // UNC paths such as \\wsl.localhost\... are often unavailable at logon.
    public static bool IsNetworkPath(string executablePath) =>
        executablePath.StartsWith(@"\\", StringComparison.Ordinal);
}
