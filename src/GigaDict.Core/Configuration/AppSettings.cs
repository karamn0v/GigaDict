using System.Text.Json.Serialization;

namespace GigaDict.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<TransportKind>))]
public enum TransportKind { Http, WebSocket }

[JsonConverter(typeof(JsonStringEnumConverter<InsertionMethod>))]
public enum InsertionMethod { UnicodeSendInput, Clipboard }

[JsonConverter(typeof(JsonStringEnumConverter<FocusProtectionMode>))]
public enum FocusProtectionMode { Strict, Compatible }

public sealed record RetrySettings
{
    public int MaxAttempts { get; init; } = 3;
    public int InitialDelaySeconds { get; init; } = 2;
    public int MaximumDelaySeconds { get; init; } = 30;
}

public sealed record AppSettings
{
    public Uri ServerBaseUri { get; init; } = new("http://127.0.0.1:9876");
    public TransportKind Transport { get; init; } = TransportKind.Http;
    public int ConnectTimeoutSeconds { get; init; } = 10;
    public int RecognitionTimeoutSeconds { get; init; } = 900;
    public RetrySettings Retry { get; init; } = new();
    public string? CaptureDeviceId { get; init; }
    public string Hotkey { get; init; } = "F8";
    public InsertionMethod InsertionMethod { get; init; } = InsertionMethod.UnicodeSendInput;
    public FocusProtectionMode FocusProtection { get; init; } = FocusProtectionMode.Strict;
    public bool AutomaticSpaces { get; init; } = true;
    public int MaximumRecordingSeconds { get; init; } = 300;
    public int MaximumQueueItems { get; init; } = 10;
    public int MaximumAudioMemoryMiB { get; init; } = 128;
    public int HistorySize { get; init; } = 10;
    public bool AutoStart { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ServerBaseUri.Scheme is not ("http" or "https")) errors.Add("Адрес сервера должен использовать HTTP или HTTPS.");
        if (!ServerBaseUri.IsAbsoluteUri) errors.Add("Адрес сервера должен быть абсолютным.");
        if (ConnectTimeoutSeconds is < 1 or > 300) errors.Add("Тайм-аут соединения: 1–300 секунд.");
        if (RecognitionTimeoutSeconds is < 10 or > 7200) errors.Add("Тайм-аут распознавания: 10–7200 секунд.");
        if (MaximumRecordingSeconds is < 1 or > 3600) errors.Add("Длительность записи: 1–3600 секунд.");
        if (MaximumQueueItems is < 1 or > 100) errors.Add("Размер очереди: 1–100.");
        if (MaximumAudioMemoryMiB is < 16 or > 2048) errors.Add("Лимит RAM: 16–2048 МиБ.");
        if (Retry.MaxAttempts is < 1 or > 10) errors.Add("Число попыток: 1–10.");
        return errors;
    }
}

