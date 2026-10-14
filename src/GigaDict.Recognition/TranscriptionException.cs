namespace GigaDict.Recognition;

public class TranscriptionException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class TransientTranscriptionException(string message, TimeSpan? retryAfter = null, Exception? innerException = null)
    : TranscriptionException(message, innerException)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

