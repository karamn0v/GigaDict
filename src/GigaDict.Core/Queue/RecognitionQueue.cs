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
    private readonly int _maxItems;
    private readonly long _maxBytes;
    private readonly int _historySize;
    private int _itemCount;
    private long _audioBytes;

    public RecognitionQueue(IEnumerable<ITranscriptionTransport> transports, int maxItems = 10, long maxBytes = 128L * 1024 * 1024, int historySize = 10)
    {
        _transports = transports.ToDictionary(x => x.Kind);
        _maxItems = maxItems;
        _maxBytes = maxBytes;
        _historySize = historySize;
        _channel = Channel.CreateBounded<DictationTask>(new BoundedChannelOptions(maxItems)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(ProcessAsync);
    }

    public event Func<DictationTask, Task>? ResultReady;
    public int Count => Volatile.Read(ref _itemCount);
    public long AudioBytes => Interlocked.Read(ref _audioBytes);
    public IReadOnlyList<HistoryEntry> History { get { lock (_gate) return _history.ToArray(); } }

    public void UpdateHistory(DictationTask item)
    {
        var entry = new HistoryEntry(item.Id, item.Sequence, item.CreatedAt, item.Result, item.Error,
            item.Status == DictationStatus.Inserted);
        lock (_gate)
        {
            var index = _history.FindIndex(x => x.TaskId == item.Id);
            if (index >= 0) _history[index] = entry;
            else _history.Insert(0, entry);
            if (_history.Count > _historySize) _history.RemoveRange(_historySize, _history.Count - _historySize);
        }
    }

    public bool TryEnqueue(DictationTask task)
    {
        var length = task.AudioLength;
        lock (_gate)
        {
            if (_itemCount >= _maxItems || _audioBytes + length > _maxBytes) return false;
            if (!_channel.Writer.TryWrite(task)) return false;
            _itemCount++;
            _audioBytes += length;
            task.TransitionTo(DictationStatus.Queued);
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
            if (item.Status is not (DictationStatus.Cancelled or DictationStatus.Failed)) item.TransitionTo(DictationStatus.Cancelled);
        }
        catch (Exception exception)
        {
            item.Error = exception.Message;
            if (item.Status is not (DictationStatus.Failed or DictationStatus.Cancelled)) item.TransitionTo(DictationStatus.Failed);
        }
        finally
        {
            UpdateHistory(item);
            lock (_gate) { _itemCount--; _audioBytes -= item.AudioLength; }
            item.ReleaseAudio();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        while (_channel.Reader.TryRead(out var item)) item.Dispose();
        _shutdown.Dispose();
    }
}
