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

    [Fact]
    public async Task ConsumerNeverSeesAnUnqueuedTask()
    {
        await using var queue = new RecognitionQueue([new ImmediateTransport()], 1, 1024, 1);
        for (var index = 0; index < 30_000; index++)
        {
            using var item = NewTask(index, 2);
            Assert.True(queue.TryEnqueue(item));
            var deadline = Environment.TickCount64 + 5000;
            while (queue.Count != 0 && Environment.TickCount64 < deadline) Thread.Yield();
            Assert.Equal(0, queue.Count);
            Assert.Equal(DictationStatus.SavedToHistory, item.Status);
            Assert.Null(item.Error);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ResultHandoffFailurePreservesResultAndWorker(bool waiting, bool cancelled)
    {
        await using var queue = new RecognitionQueue([new ImmediateTransport()], 2, 1024, 10);
        queue.ResultReady += item =>
        {
            if (item.Sequence != 1) return Task.CompletedTask;
            if (waiting) item.TransitionTo(DictationStatus.WaitingForInsertion);
            return cancelled ? Task.FromCanceled(new CancellationToken(true)) : Task.FromException(new InvalidOperationException("handoff failed"));
        };
        using var first = new DictationTask(1, [0, 0], new FocusIdentity(1, 2, "editable", true, false), new AppSettings());
        using var second = NewTask(2, 2);
        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));
        await WaitUntilAsync(() => queue.Count == 0);
        Assert.Equal(DictationStatus.SavedToHistory, first.Status);
        Assert.False(first.AutomaticInsertionAllowed);
        Assert.Equal("ok", first.Result);
        Assert.Equal(DictationStatus.SavedToHistory, second.Status);
        Assert.Equal(0, queue.AudioBytes);
    }

    [Fact]
    public async Task LimitsChangeImmediatelyWithoutReplacingWorkerOrDroppingAcceptedAudio()
    {
        var transport = new FakeTransport(block: true);
        await using var queue = new RecognitionQueue([transport], 1, 10, 10);
        using var first = NewTask(1, 10);
        using var second = NewTask(2, 10);
        using var rejected = NewTask(3, 1);
        var order = new List<long>();
        queue.ResultReady += item => { order.Add(item.Sequence); return Task.CompletedTask; };
        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(second));
        queue.UpdateLimits(2, 20, 1);
        Assert.True(queue.TryEnqueue(second));
        queue.UpdateLimits(1, 5, 1);
        Assert.False(queue.TryEnqueue(rejected));
        Assert.Equal(DictationStatus.Recording, rejected.Status);
        Assert.Equal(20, queue.AudioBytes);
        transport.Release();
        await WaitUntilAsync(() => queue.Count == 0);
        Assert.Equal([1, 2], order);
        Assert.Equal(1, transport.MaximumConcurrency);
        Assert.Single(queue.History);
        Assert.True(queue.TryEnqueue(rejected));
        await WaitUntilAsync(() => queue.Count == 0);
        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task DisposingQueueClearsAccountingAndRejectsNewTasks()
    {
        var queue = new RecognitionQueue([new FakeTransport(block: true)], 2, 1024, 10);
        using var first = NewTask(1, 10);
        using var second = NewTask(2, 10);
        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));
        await queue.DisposeAsync();
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.AudioBytes);
        Assert.Equal(0, first.AudioLength);
        Assert.Equal(0, second.AudioLength);
        using var rejected = NewTask(3, 2);
        Assert.False(queue.TryEnqueue(rejected));
        Assert.Equal(DictationStatus.Recording, rejected.Status);
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

    private sealed class ImmediateTransport : ITranscriptionTransport
    {
        public TransportKind Kind => TransportKind.Http;
        public Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new RecognitionResult("ok"));
    }
}

