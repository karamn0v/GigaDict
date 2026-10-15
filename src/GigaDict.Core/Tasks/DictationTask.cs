using GigaDict.Core.Configuration;

namespace GigaDict.Core.Tasks;

public enum DictationStatus
{
    Recording, Queued, Recognizing, RetryWaiting, Recognized,
    WaitingForInsertion, Inserted, SavedToHistory, Failed, Cancelled
}

public sealed record FocusIdentity(nint WindowHandle, int ProcessId, string RuntimeId, bool IsEditable, bool IsPassword);

public sealed record RecognitionRequest(
    Guid TaskId,
    ReadOnlyMemory<byte> Pcm16,
    Uri ServerBaseUri,
    TransportKind Transport,
    TimeSpan ConnectTimeout,
    TimeSpan RecognitionTimeout);

public sealed record RecognitionResult(string Text, bool WasRetried = false);

public interface ITranscriptionTransport
{
    TransportKind Kind { get; }
    Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken);
}

public sealed class DictationTask : IDisposable
{
    private static readonly Dictionary<DictationStatus, HashSet<DictationStatus>> Allowed =
        new Dictionary<DictationStatus, HashSet<DictationStatus>>
        {
            [DictationStatus.Recording] = [DictationStatus.Queued, DictationStatus.Cancelled, DictationStatus.Failed],
            [DictationStatus.Queued] = [DictationStatus.Recognizing, DictationStatus.Cancelled, DictationStatus.Failed],
            [DictationStatus.Recognizing] = [DictationStatus.RetryWaiting, DictationStatus.Recognized, DictationStatus.Failed, DictationStatus.Cancelled],
            [DictationStatus.RetryWaiting] = [DictationStatus.Recognizing, DictationStatus.Failed, DictationStatus.Cancelled],
            [DictationStatus.Recognized] = [DictationStatus.WaitingForInsertion, DictationStatus.SavedToHistory],
            [DictationStatus.WaitingForInsertion] = [DictationStatus.Inserted, DictationStatus.SavedToHistory],
            [DictationStatus.Inserted] = [DictationStatus.SavedToHistory],
            [DictationStatus.SavedToHistory] = [], [DictationStatus.Failed] = [], [DictationStatus.Cancelled] = []
        };

    private readonly object _gate = new();
    private byte[]? _audio;
    private bool _disposed;
    private DictationStatus _status = DictationStatus.Recording;

    public DictationTask(long sequence, byte[] audio, FocusIdentity? focus, AppSettings settings, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Sequence = sequence;
        CreatedAt = DateTimeOffset.UtcNow;
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        Focus = focus;
        Settings = settings;
        AutomaticInsertionAllowed = focus is { IsPassword: false }
            && (settings.FocusProtection == FocusProtectionMode.Compatible || focus.IsEditable);
    }

    public Guid Id { get; }
    public long Sequence { get; }
    public DateTimeOffset CreatedAt { get; }
    public FocusIdentity? Focus { get; }
    public AppSettings Settings { get; }
    public bool AutomaticInsertionAllowed { get; private set; }
    public bool WasRetried { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
    public DictationStatus Status { get { lock (_gate) return _status; } }
    public int AudioLength { get { lock (_gate) return _audio?.Length ?? 0; } }

    public ReadOnlyMemory<byte> Audio
    {
        get { lock (_gate) return _audio ?? throw new ObjectDisposedException(nameof(DictationTask)); }
    }

    public void InvalidateInsertion() => AutomaticInsertionAllowed = false;

    public void EndRecognitionWithError(string? error, bool cancelled)
    {
        lock (_gate)
        {
            AutomaticInsertionAllowed = false;
            Error = error;
            _status = _status switch
            {
                DictationStatus.Recording or DictationStatus.Queued or DictationStatus.Recognizing or DictationStatus.RetryWaiting
                    => cancelled ? DictationStatus.Cancelled : DictationStatus.Failed,
                DictationStatus.Recognized or DictationStatus.WaitingForInsertion => DictationStatus.SavedToHistory,
                _ => _status
            };
        }
    }

    public void TransitionTo(DictationStatus next)
    {
        lock (_gate)
        {
            if (!Allowed[_status].Contains(next))
                throw new InvalidOperationException($"Недопустимый переход {_status} → {next}.");
            _status = next;
        }
    }

    public void ReleaseAudio()
    {
        lock (_gate)
        {
            if (_audio is not null) Array.Clear(_audio);
            _audio = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { Cancellation.Cancel(); }
        finally { Cancellation.Dispose(); ReleaseAudio(); }
    }
}

