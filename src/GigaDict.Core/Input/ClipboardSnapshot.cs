namespace GigaDict.Core.Input;

public static class ClipboardSnapshot
{
    // The data object OLE returns for another owner's clipboard is live: after the
    // clipboard is replaced it enumerates the replacement. Copy every readable
    // format before replacing; unreadable or unrenderable formats are skipped.
    public static IReadOnlyList<KeyValuePair<string, object>> Capture(IEnumerable<string> formats, Func<string, object?> read)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(read);
        var result = new List<KeyValuePair<string, object>>();
        foreach (var format in formats.Distinct(StringComparer.Ordinal))
        {
            object? data;
            try { data = read(format); }
#pragma warning disable CA1031 // A third-party format may fail in any way; the rest must still be preserved.
            catch (Exception exception) when (exception is not OperationCanceledException) { continue; }
#pragma warning restore CA1031
            if (data is not null) result.Add(new(format, data));
        }
        return result;
    }
}

public static class ClipboardPrivacyFormats
{
    public const string ExcludeFromMonitorProcessing = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudClipboard = "CanUploadToCloudClipboard";

    // Recognized text must not reach Win+V history, Cloud Clipboard or clipboard
    // managers that persist to disk. Windows reads the last two formats as a DWORD;
    // zero opts the item out.
    public static IReadOnlyList<KeyValuePair<string, byte[]>> Create() =>
    [
        new(ExcludeFromMonitorProcessing, new byte[4]),
        new(CanIncludeInClipboardHistory, new byte[4]),
        new(CanUploadToCloudClipboard, new byte[4])
    ];
}
