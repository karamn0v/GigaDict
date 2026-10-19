using GigaDict.Core.Configuration;
using GigaDict.Core.Tasks;

namespace GigaDict.Recognition;

public sealed class RetryingTransport(ITranscriptionTransport inner, RetrySettings settings) : ITranscriptionTransport
{
    public async Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await inner.TranscribeAsync(request, cancellationToken).ConfigureAwait(false);
                return result with { WasRetried = attempt > 1 };
            }
            catch (TransientTranscriptionException exception) when (attempt < settings.MaxAttempts)
            {
                var exponential = TimeSpan.FromSeconds(Math.Min(settings.MaximumDelaySeconds,
                    settings.InitialDelaySeconds * Math.Pow(2, attempt - 1)));
                await Task.Delay(exception.RetryAfter ?? exponential, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
