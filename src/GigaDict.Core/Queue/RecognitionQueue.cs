using System.Threading.Channels;
using GigaDict.Core.Configuration;
using GigaDict.Core.Tasks;

namespace GigaDict.Core.Queue;

public sealed record HistoryEntry(Guid TaskId, long Sequence, DateTimeOffset CreatedAt, string? Text, string? Error, bool Inserted);

public sealed class RecognitionQueue : IAsyncDisposable
{
    private readonly Channel<DictationTask> _channel;
    private readonly Dictionary<TransportKind, ITranscriptionTransport> _transports;
    private readonly List<HistoryEntry> _history = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private int _maxItems;
    private long _maxBytes;
    private int _historySize;
    private bool _accepting = true;
    private int _itemCount;
    private long _audioBytes;

    public RecognitionQueue(IEnumerable<ITranscriptionTransport> transports, int maxItems = 10, long maxBytes = 128L * 1024 * 1024, int historySize = 10)
    {
        _transports = transports.ToDictionary(x => x.Kind);
        UpdateLimits(maxItems, maxBytes, historySize);
        // Admission and memory accounting are guarded by _gate. An unbounded
        // channel lets limits change without replacing the worker or dropping items.
        _channel = Channel.CreateUnbounded<DictationTask>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(ProcessAsync);
    }

    public event Func<DictationTask, Task>? ResultReady;
    public int Count => Volatile.Read(ref _itemCount);
    public long AudioBytes => Interlocked.Read(ref _audioBytes);
    public IReadOnlyList<HistoryEntry> History { get { lock (_gate) return _history.ToArray(); } }

    public void UpdateLimits(int maxItems, long maxBytes, int historySize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(historySize);
        lock (_gate)
        {
            _maxItems = maxItems;
            _maxBytes = maxBytes;
            _historySize = historySize;
            TrimHistory();
        }
    }

    private void TrimHistory()
    {
        if (_history.Count > _historySize) _history.RemoveRange(_historySize, _history.Count - _historySize);
    }

    public void UpdateHistory(DictationTask item)
    {
        var entry = new HistoryEntry(item.Id, item.Sequence, item.CreatedAt, item.Result, item.Error,
            item.Status == DictationStatus.Inserted);
        lock (_gate)
        {
            var index = _history.FindIndex(x => x.TaskId == item.Id);
            if (index >= 0) _history[index] = entry;
            else _history.Insert(0, entry);
            TrimHistory();
        }
    }

    public bool TryEnqueue(DictationTask task)
    {
        var length = task.AudioLength;
        lock (_gate)
        {
            if (!_accepting || _itemCount >= _maxItems || _audioBytes + length > _maxBytes) return false;
            task.TransitionTo(DictationStatus.Queued);
            _itemCount++;
            _audioBytes += length;
            if (!_channel.Writer.TryWrite(task))
            {
                _itemCount--;
                _audioBytes -= length;
                task.TransitionTo(DictationStatus.Cancelled);
                return false;
            }
            return true;
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
                await ProcessOneAsync(item, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task ProcessOneAsync(DictationTask item, CancellationToken shutdown)
    {
        var audioLength = item.AudioLength;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown, item.Cancellation.Token);
        try
        {
            item.TransitionTo(DictationStatus.Recognizing);
            var request = new RecognitionRequest(item.Id, item.Audio, item.Settings.ServerBaseUri, item.Settings.Transport,
                TimeSpan.FromSeconds(item.Settings.ConnectTimeoutSeconds), TimeSpan.FromSeconds(item.Settings.RecognitionTimeoutSeconds));
            var result = await _transports[item.Settings.Transport].TranscribeAsync(request, linked.Token).ConfigureAwait(false);
            item.Result = result.Text;
            item.WasRetried = result.WasRetried;
            item.TransitionTo(DictationStatus.Recognized);
            if (ResultReady is { } handler) await handler(item).ConfigureAwait(false);
            if (item.Status == DictationStatus.Recognized)
                item.TransitionTo(DictationStatus.SavedToHistory);
        }
        catch (OperationCanceledException)
        {
            item.EndRecognitionWithError(null, cancelled: true);
        }
        catch (Exception exception)
        {
            item.EndRecognitionWithError(exception.Message, cancelled: false);
        }
        finally
        {
            UpdateHistory(item);
            item.ReleaseAudio();
            lock (_gate) { _itemCount--; _audioBytes -= audioLength; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _accepting = false;
            _channel.Writer.TryComplete();
        }
        _shutdown.Cancel();
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        while (_channel.Reader.TryRead(out var item))
        {
            var length = item.AudioLength;
            item.EndRecognitionWithError(null, cancelled: true);
            item.Dispose();
            lock (_gate) { _itemCount--; _audioBytes -= length; }
        }
        _shutdown.Dispose();
    }
}
