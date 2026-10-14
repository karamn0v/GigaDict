using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GigaDict.Core.Configuration;
using GigaDict.Core.Tasks;

namespace GigaDict.Recognition;

public sealed class WebSocketTranscriptionTransport(Func<ClientWebSocket>? socketFactory = null) : ITranscriptionTransport
{
    private readonly Func<ClientWebSocket> _socketFactory = socketFactory ?? (() => new ClientWebSocket());
    public TransportKind Kind => TransportKind.WebSocket;

    public async Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.RecognitionTimeout);
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        connectTimeout.CancelAfter(request.ConnectTimeout);
        using var socket = _socketFactory();
        var uri = BuildUri(request.ServerBaseUri);
        try
        {
            await socket.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
            using var ready = await ReceiveJsonAsync(socket, timeout.Token).ConfigureAwait(false);
            ValidateReady(ready);
            await SendJsonAsync(socket, new { type = "configure", protocol_version = "1.0", sample_rate = 16000, endpoint_mode = "manual" }, timeout.Token).ConfigureAwait(false);

            const int frameBytes = 32_000; // one second of PCM16/16 kHz/mono
            for (var offset = 0; offset < request.Pcm16.Length; offset += frameBytes)
            {
                var length = Math.Min(frameBytes, request.Pcm16.Length - offset);
                await socket.SendAsync(request.Pcm16.Slice(offset, length), WebSocketMessageType.Binary, true, timeout.Token).ConfigureAwait(false);
            }
            await SendJsonAsync(socket, new { type = "stop" }, timeout.Token).ConfigureAwait(false);

            var finalized = new List<string>();
            while (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var message = await ReceiveJsonAsync(socket, timeout.Token).ConfigureAwait(false);
                var type = GetString(message, "type");
                switch (type)
                {
                    case "partial":
                        break;
                    case "error":
                        ThrowServerError(message);
                        break;
                    case "final":
                        var text = GetString(message, "text")?.Trim();
                        if (!string.IsNullOrEmpty(text)) finalized.Add(text);
                        if (GetString(message, "endpoint_reason") == "stop")
                        {
                            var joined = string.Join(' ', finalized);
                            if (joined.Length == 0) throw new TranscriptionException("GigaSTT вернул пустой результат.");
                            await CloseNormallyAsync(socket, timeout.Token).ConfigureAwait(false);
                            return new RecognitionResult(joined);
                        }
                        break;
                    default:
                        throw new TranscriptionException($"Неизвестное сообщение WebSocket: {type ?? "без типа"}.");
                }
            }
            throw new TransientTranscriptionException("WebSocket закрыт до итогового результата.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransientTranscriptionException("Истекло время ожидания WebSocket-распознавания.", null, exception);
        }
        catch (WebSocketException exception)
        {
            throw new TransientTranscriptionException("Соединение WebSocket с GigaSTT прервано.", null, exception);
        }
    }

    private static Uri BuildUri(Uri baseUri)
    {
        var builder = new UriBuilder(baseUri) { Scheme = baseUri.Scheme == "https" ? "wss" : "ws", Path = "/v1/ws", Query = "" };
        return builder.Uri;
    }

    private static void ValidateReady(JsonDocument message)
    {
        if (GetString(message, "type") != "ready") throw new TranscriptionException("GigaSTT не прислал ready.");
        var version = GetString(message, "version");
        if (version is null || !version.StartsWith("1.", StringComparison.Ordinal))
            throw new TranscriptionException($"Несовместимая версия WebSocket-протокола: {version ?? "не указана"}.");
        if (!message.RootElement.TryGetProperty("supported_rates", out var rates) ||
            !rates.EnumerateArray().Any(x => x.TryGetInt32(out var rate) && rate == 16000))
            throw new TranscriptionException("GigaSTT не поддерживает частоту 16000 Гц.");
    }

    private static void ThrowServerError(JsonDocument message)
    {
        var code = GetString(message, "code") ?? "unknown";
        var description = GetString(message, "message") ?? "Ошибка GigaSTT";
        TimeSpan? retry = null;
        if (message.RootElement.TryGetProperty("retry_after_ms", out var retryNode) && retryNode.TryGetInt32(out var milliseconds))
            retry = TimeSpan.FromMilliseconds(milliseconds);
        if (code is "timeout" or "pool_closed" or "idle_timeout" or "inference_timeout")
            throw new TransientTranscriptionException($"{description} ({code}).", retry);
        throw new TranscriptionException($"{description} ({code}).");
    }

    private static string? GetString(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;

    private static async Task SendJsonAsync(ClientWebSocket socket, object value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new TransientTranscriptionException("GigaSTT закрыл WebSocket до завершения.");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new TranscriptionException("Ожидалось текстовое сообщение WebSocket.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        try { return JsonDocument.Parse(stream.ToArray()); }
        catch (JsonException exception) { throw new TranscriptionException("Некорректный JSON WebSocket.", exception); }
    }

    private static async Task CloseNormallyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State == WebSocketState.Open)
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "complete", cancellationToken).ConfigureAwait(false);
    }
}
