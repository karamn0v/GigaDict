using NAudio.CoreAudioApi;
using NAudio.Wave;
using GigaDict.Core.Tasks;

namespace GigaDict.Audio;

public sealed record CaptureDevice(string Id, string Name, bool IsDefault);

public sealed class WasapiRecorder(Func<string?, IWaveIn>? captureFactory = null, TimeSpan? stopTimeout = null) : IAudioRecorder, IDisposable
{
    private readonly Func<string?, IWaveIn> _captureFactory = captureFactory ?? CreateCapture;
    private readonly TimeSpan _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private IWaveIn? _capture;
    private MemoryStream? _raw;
    private WaveFormat? _format;
    private long _maximumRawBytes;
    private TaskCompletionSource _stopped = NewCompletion();
    private bool _limitReached;

    public event EventHandler? LimitReached;
    public event EventHandler<Exception>? CaptureFailed;

    public static IReadOnlyList<CaptureDevice> EnumerateDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = TryGetDefault(enumerator)?.ID;
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(x => new CaptureDevice(x.ID, x.FriendlyName, x.ID == defaultId)).ToArray();
    }

    public void Start(string? deviceId, TimeSpan maximumDuration, long maximumPcmBytes)
    {
        lock (_gate)
        {
            if (_capture is not null) throw new InvalidOperationException("Запись уже идёт.");
            _capture = _captureFactory(deviceId);
            _format = _capture.WaveFormat;
            var estimatedRaw = checked((long)(_format.AverageBytesPerSecond * Math.Ceiling(maximumDuration.TotalSeconds)));
            _maximumRawBytes = Math.Min(estimatedRaw, maximumPcmBytes * Math.Max(1, _format.AverageBytesPerSecond / 32000));
            _raw = new MemoryStream((int)Math.Min(_maximumRawBytes, 16 * 1024 * 1024));
            _stopped = NewCompletion();
            _limitReached = false;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }
    }

    public async Task<byte[]> StopAsync(CancellationToken cancellationToken = default)
    {
        IWaveIn capture;
        Task stopped;
        lock (_gate)
        {
            capture = _capture ?? throw new InvalidOperationException("Запись не идёт.");
            stopped = _stopped.Task;
        }
        capture.StopRecording();
        try { await stopped.WaitAsync(_stopTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // A driver that never reports RecordingStopped must not block the
            // sequential command queue. Dispose joins the hung capture thread, so
            // run it in the background after detaching the capture.
            IWaveIn? hung;
            lock (_gate) hung = ReferenceEquals(capture, _capture) ? DetachCapture() : null;
            if (hung is not null) _ = Task.Run(hung.Dispose, CancellationToken.None);
            throw new TimeoutException("Микрофон не остановился вовремя.");
        }
        byte[] raw;
        WaveFormat format;
        lock (_gate)
        {
            if (!ReferenceEquals(capture, _capture)) throw new InvalidOperationException("Запись отменена.");
            raw = _raw?.ToArray() ?? [];
            format = _format ?? throw new InvalidOperationException();
            DetachCapture();
        }
        try
        {
            // NAudio Dispose waits for the capture thread. Never hold the lock
            // needed by its callbacks while joining that thread or resampling.
            capture.Dispose();
            return Pcm16Converter.Convert(raw, format);
        }
        finally { Array.Clear(raw); }
    }

    public void Cancel()
    {
        IWaveIn? capture;
        lock (_gate)
        {
            capture = DetachCapture();
            _stopped.TrySetCanceled();
        }
        capture?.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (_gate)
        {
            if (_raw is null || _capture is null || !ReferenceEquals(sender, _capture)) return;
            var remaining = _maximumRawBytes - _raw.Length;
            if (remaining <= 0)
            {
                ReachLimit();
                return;
            }
            _raw.Write(args.Buffer, 0, (int)Math.Min(args.BytesRecorded, remaining));
            if (_raw.Length >= _maximumRawBytes) ReachLimit();
        }
    }

    private void ReachLimit()
    {
        if (_limitReached) return;
        _limitReached = true;
        // The subscriber only posts an ordered stop command. Do not schedule an
        // independent callback that could arrive after the next recording starts.
        LimitReached?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        TaskCompletionSource stopped;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _capture)) return;
            stopped = _stopped;
        }
        if (args.Exception is not null)
        {
            stopped.TrySetException(args.Exception);
            CaptureFailed?.Invoke(this, args.Exception);
        }
        else stopped.TrySetResult();
    }

    private IWaveIn? DetachCapture()
    {
        var capture = _capture;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
        }
        _capture = null;
        if (_raw is not null) _raw.GetBuffer().AsSpan(0, checked((int)_raw.Length)).Clear();
        _raw?.Dispose();
        _raw = null;
        _format = null;
        return capture;
    }

    private static IWaveIn CreateCapture(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = deviceId is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : enumerator.GetDevice(deviceId);
        return new WasapiCapture(device);
    }

    private static MMDevice? TryGetDefault(MMDeviceEnumerator enumerator)
    {
        try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return null; }
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() => Cancel();
}
