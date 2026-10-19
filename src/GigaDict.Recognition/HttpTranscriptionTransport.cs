using System.Net;
using System.Text.Json;
using GigaDict.Core.Tasks;

namespace GigaDict.Recognition;

public sealed class HttpTranscriptionTransport(HttpClient? httpClient = null) : ITranscriptionTransport
{
    public async Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken)
    {
        using var ownedClient = httpClient is null ? CreateClient(request.ConnectTimeout) : null;
        var client = httpClient ?? ownedClient!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.RecognitionTimeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(request.ServerBaseUri, "/v1/transcribe"))
        {
            Content = new PcmWaveContent(request.Pcm16, true)
        };
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // After a full recognition timeout the single inference slot may still be
            // busy with this audio; retrying would block the FIFO for another period.
            // Only a connect timeout (nothing was processed) is worth repeating.
            if (timeout.IsCancellationRequested) throw RecognitionTimedOut(exception);
            throw new TransientTranscriptionException("Истекло время подключения к GigaSTT.", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TransientTranscriptionException("Не удалось подключиться к GigaSTT.", null, exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var retry = response.Headers.RetryAfter?.Delta;
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                    throw new TransientTranscriptionException($"GigaSTT временно недоступен ({(int)response.StatusCode}).", retry);
                if (response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity)
                    throw new TranscriptionException($"GigaSTT отклонил аудио ({(int)response.StatusCode}).");
                throw new TranscriptionException($"Ошибка GigaSTT HTTP {(int)response.StatusCode}.");
            }

            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token).ConfigureAwait(false);
                if (!json.RootElement.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String)
                    throw new TranscriptionException("Ответ GigaSTT не содержит поле text.");
                var text = textNode.GetString()?.Trim();
                if (string.IsNullOrEmpty(text)) throw new TranscriptionException("GigaSTT вернул пустой результат.");
                return new RecognitionResult(text);
            }
            catch (JsonException exception)
            {
                throw new TranscriptionException("GigaSTT вернул некорректный JSON.", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw RecognitionTimedOut(exception);
            }
        }
    }

    private static TranscriptionException RecognitionTimedOut(Exception inner) =>
        new("Истекло время ожидания распознавания.", inner);

    internal static HttpClient CreateClient(TimeSpan connectTimeout) =>
        new(new SocketsHttpHandler { ConnectTimeout = connectTimeout }) { Timeout = Timeout.InfiniteTimeSpan };
}

