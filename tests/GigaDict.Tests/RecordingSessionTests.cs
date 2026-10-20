using GigaDict.Core.Configuration;
using GigaDict.Core.Input;
using GigaDict.Core.Queue;
using GigaDict.Core.Tasks;

namespace GigaDict.Tests;

public sealed class RecordingSessionTests
{
    [Fact]
    public async Task RapidReleaseAndPressWaitForCaptureShutdownAndKeepOriginalSnapshot()
    {
        var recorder = new FakeRecorder();
        var focus = new FakeFocus();
        var controller = new RecordingSessionController(recorder, focus);
        var settings = new AppSettings { CaptureDeviceId = "original", InsertionMethod = InsertionMethod.Clipboard };
        var originalFocus = focus.Current;
        DictationTask? completed = null;
        var errors = new List<Exception>();
        await using var actions = new SequentialActionQueue(e => { errors.Add(e); return Task.CompletedTask; });
        actions.Start();
        var keys = new HotkeyStateMachine(new HashSet<int> { 0x77 });
        void Dispatch(bool down)
        {
            var decision = keys.ProcessKey(0x77, down, injected: false);
            actions.TryPost(async () =>
            {
                if (decision.Action == HotkeyAction.StartRecording) controller.Start(settings);
                else if (decision.Action == HotkeyAction.StopRecording) completed = await controller.CompleteAsync();
            });
        }

        Dispatch(down: true);
        Dispatch(down: false);
        await recorder.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(controller.IsRecording);
        Assert.Equal(1, recorder.Starts);
        settings = settings with { CaptureDeviceId = "next", InsertionMethod = InsertionMethod.UnicodeSendInput };
        focus.Current = originalFocus with { RuntimeId = "next" };
        Dispatch(down: true);
        Assert.Equal(1, recorder.Starts);
        recorder.Stopped.SetResult(new byte[RecordingSessionController.MinimumAudioBytes]);
        await actions.DisposeAsync();
        using var item = Assert.IsType<DictationTask>(completed);
        Assert.Empty(errors);
        Assert.Equal(2, recorder.Starts);
        Assert.Equal("original", item.Settings.CaptureDeviceId);
        Assert.Equal(InsertionMethod.Clipboard, item.Settings.InsertionMethod);
        Assert.Equal(originalFocus, item.Focus);
        Assert.False(item.AutomaticInsertionAllowed);
        controller.Cancel();
    }

    [Fact]
    public void FailedStartRollsBackSessionAndFocusTracking()
    {
        var recorder = new FakeRecorder { FailStart = true };
        var focus = new FakeFocus();
        var controller = new RecordingSessionController(recorder, focus);
        Assert.Throws<InvalidOperationException>(() => controller.Start(new AppSettings()));
        Assert.False(controller.IsRecording);
        Assert.Empty(focus.Tracked);
        Assert.False(recorder.Active);
        recorder.FailStart = false;
        controller.Start(new AppSettings());
        Assert.True(controller.IsRecording);
        controller.Cancel();
    }

    [Fact]
    public async Task CancelledStopReleasesCaptureAndAllowsNextRecording()
    {
        var recorder = new FakeRecorder();
        var focus = new FakeFocus();
        var controller = new RecordingSessionController(recorder, focus);
        controller.Start(new AppSettings());
        using var cancellation = new CancellationTokenSource();
        var stop = controller.CompleteAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        Assert.False(controller.IsRecording);
        Assert.False(recorder.Active);
        Assert.Empty(focus.Tracked);
        controller.Start(new AppSettings());
        controller.Cancel();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RecordingSessionController.MinimumAudioBytes - 2)]
    public async Task AccidentalTapIsDroppedAndClearedWithoutQueueing(int length)
    {
        var recorder = new FakeRecorder();
        var focus = new FakeFocus();
        var controller = new RecordingSessionController(recorder, focus);
        controller.Start(new AppSettings());
        var audio = Enumerable.Repeat((byte)7, length).ToArray();
        recorder.Stopped.SetResult(audio);
        Assert.Null(await controller.CompleteAsync());
        Assert.False(controller.IsRecording);
        Assert.All(audio, value => Assert.Equal(0, value));
    }

    private sealed class FakeRecorder : IAudioRecorder
    {
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<byte[]> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts { get; private set; }
        public bool Active { get; private set; }
        public bool FailStart { get; set; }
        public void Start(string? deviceId, TimeSpan maximumDuration, long maximumPcmBytes)
        {
            if (Active || FailStart) throw new InvalidOperationException("capture unavailable");
            Active = true;
            Starts++;
        }
        public async Task<byte[]> StopAsync(CancellationToken cancellationToken = default)
        {
            Stopping.TrySetResult();
            var audio = await Stopped.Task.WaitAsync(cancellationToken);
            Active = false;
            return audio;
        }
        public void Cancel() => Active = false;
    }

    private sealed class FakeFocus : IFocusGuard
    {
        public FocusIdentity Current { get; set; } = new(10, 20, "original", true, false);
        public Dictionary<Guid, FocusIdentity> Tracked { get; } = [];
        public FocusIdentity Capture() => Current;
        public void Track(Guid taskId, FocusIdentity identity, FocusProtectionMode mode = FocusProtectionMode.Strict) => Tracked.Add(taskId, identity);
        public bool IsStillSafe(Guid taskId) => Tracked.TryGetValue(taskId, out var identity) && identity == Current;
        public void Forget(Guid taskId) => Tracked.Remove(taskId);
    }
}
