namespace GigaDict.Core.Configuration;

public static class CaptureDeviceSelection
{
    public const string UnavailableName = "Выбранный микрофон (сейчас не подключён)";

    // A saved microphone that is unplugged right now must survive re-saving the
    // settings instead of silently becoming the Windows default device.
    public static bool IsUnavailable(string? savedId, IEnumerable<string?> availableIds) =>
        savedId is not null && !availableIds.Contains(savedId, StringComparer.Ordinal);
}
