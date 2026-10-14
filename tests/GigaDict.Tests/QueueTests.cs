using GigaDict.Core.Configuration;
using GigaDict.Core.Queue;
using GigaDict.Core.Tasks;

namespace GigaDict.Tests;

public sealed class QueueTests
{
    [Fact]
    public async Task ProcessesFifoWithSingleConcurrentRecognition()
    {
        var transport = new FakeTransport();
        await using var queue = new RecognitionQueue([transport], 10, 1024, 10);
        var received = new List<long>();
        queue.ResultReady += task => { received.Add(task.Sequence); task.TransitionTo(DictationStatus.SavedToHistory); return Task.CompletedTask; };
        for (var i = 1; i <= 3; i++) Assert.True(queue.TryEnqueue(NewTask(i, 10)));
        await WaitUntilAsync(() => queue.Count == 0);
        Assert.Equal([1, 2, 3], received);
        Assert.Equal(1, transport.MaximumConcurrency);
    }

    [Fact]
    public async Task EnforcesItemAndMemoryLimits()
    {
        var transport = new FakeTransport(block: true);
        await using var queue = new RecognitionQueue([transport], 2, 15, 10);
        Assert.True(queue.TryEnqueue(NewTask(1, 10)));
        Assert.False(queue.TryEnqueue(NewTask(2, 10)));
        transport.Release();
    }

    [Fact]
    public async Task HistoryUpdateExposesInsertionError()
    {
        await using var queue = new RecognitionQueue([new FakeTransport()], 10, 1024, 10);
        using var task = NewTask(1, 2);
        task.Error = "Ошибка вставки: тест";
        queue.UpdateHistory(task);
        Assert.Equal("Ошибка вставки: тест", Assert.Single(queue.History).Error);
    }

    private static DictationTask NewTask(long sequence, int bytes) => new(sequence, new byte[bytes], null, new AppSettings());
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeTransport(bool block = false) : ITranscriptionTransport
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        public int MaximumConcurrency { get; private set; }
        public TransportKind Kind => TransportKind.Http;
        public async Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try { if (block) await _release.Task.WaitAsync(cancellationToken); else await Task.Delay(5, cancellationToken); return new("ok"); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Release() => _release.TrySetResult();
    }
}

