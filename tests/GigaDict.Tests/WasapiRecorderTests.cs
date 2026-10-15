using GigaDict.Audio;
using NAudio.Wave;

namespace GigaDict.Tests;

// Exercise callback/lifecycle ordering without loading WASAPI or a real device.
public sealed class WasapiRecorderTests
{
    [Fact]
    public void CancelDoesNotHoldCallbackLockWhileDisposingCapture()
    {
        var capture = new FakeCapture();
        using var recorder = new WasapiRecorder(_ => capture);
        recorder.Start(null, TimeSpan.FromSeconds(1), 32000);
        var callback = capture.SnapshotDataCallback();
        capture.OnDispose = () =>
        {
            var inFlight = Task.Run(() => callback(capture, new WaveInEventArgs(new byte[2], 2)));
            Assert.True(inFlight.Wait(TimeSpan.FromSeconds(5)), "Capture callback blocked during disposal.");
        };
        recorder.Cancel();
        Assert.True(capture.Disposed);
    }

    [Fact]
    public async Task StaleCallbacksCannotStopOrAppendToNextCapture()
    {
        var first = new FakeCapture();
        var second = new FakeCapture { StopImmediately = false };
        var captures = new Queue<FakeCapture>([first, second]);
        using var recorder = new WasapiRecorder(_ => captures.Dequeue());
        recorder.Start(null, TimeSpan.FromSeconds(1), 32000);
        var staleData = first.SnapshotDataCallback();
        var staleStop = first.SnapshotStoppedCallback();
        recorder.Cancel();
        recorder.Start(null, TimeSpan.FromSeconds(1), 32000);
        second.Emit(new byte[4]);
        var stop = recorder.StopAsync();
        staleData(first, new WaveInEventArgs(new byte[10], 10));
        staleStop(first, new StoppedEventArgs());
        Assert.False(stop.IsCompleted);
        second.Finish();
        var pcm = await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, pcm.Length);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task LimitCallbackOnlyNotifiesAndShutdownIsDoneByConsumer()
    {
        var capture = new FakeCapture();
        using var recorder = new WasapiRecorder(_ => capture);
        var notifications = 0;
        recorder.LimitReached += (_, _) => notifications++;
        recorder.Start(null, TimeSpan.FromSeconds(1), 2);
        capture.Emit(new byte[4]);
        capture.Emit(new byte[4]);
        Assert.Equal(1, notifications);
        Assert.Equal(0, capture.StopCalls);
        Assert.Equal(2, (await recorder.StopAsync()).Length);
        Assert.Equal(1, capture.StopCalls);
    }

    [Fact]
    public async Task CaptureFailureCannotReturnPartialAudioAsSuccessfulRecording()
    {
        var capture = new FakeCapture();
        using var recorder = new WasapiRecorder(_ => capture);
        recorder.Start(null, TimeSpan.FromSeconds(1), 32000);
        capture.Emit(new byte[4]);
        capture.Finish(new InvalidOperationException("device disconnected"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StopAsync());
        recorder.Cancel();
        Assert.True(capture.Disposed);
    }

    private sealed class FakeCapture : IWaveIn
    {
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public WaveFormat WaveFormat { get; set; } = new(16000, 16, 1);
        public bool StopImmediately { get; init; } = true;
        public bool Disposed { get; private set; }
        public int StopCalls { get; private set; }
        public Action? OnDispose { get; set; }
        public void StartRecording() { }
        public void StopRecording() { StopCalls++; if (StopImmediately) Finish(); }
        public void Emit(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
        public void Finish(Exception? exception = null) => RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));
        public EventHandler<WaveInEventArgs> SnapshotDataCallback() => DataAvailable!;
        public EventHandler<StoppedEventArgs> SnapshotStoppedCallback() => RecordingStopped!;
        public void Dispose() { OnDispose?.Invoke(); Disposed = true; }
    }
}
