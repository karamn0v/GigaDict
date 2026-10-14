using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GigaDict.Audio;

public sealed record CaptureDevice(string Id, string Name, bool IsDefault);

public sealed class WasapiRecorder : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
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
            using var enumerator = new MMDeviceEnumerator();
            var device = deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                : enumerator.GetDevice(deviceId);
            _capture = new WasapiCapture(device);
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
        WasapiCapture capture;
        lock (_gate) capture = _capture ?? throw new InvalidOperationException("Запись не идёт.");
        capture.StopRecording();
        await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var pcm = Pcm16Converter.Convert(_raw?.ToArray() ?? [], _format ?? throw new InvalidOperationException());
            Cleanup();
            return pcm;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _capture?.StopRecording();
            if (_raw is not null)
            {
                _raw.GetBuffer().AsSpan(0, checked((int)_raw.Length)).Clear();
                _raw.Dispose();
                _raw = null;
            }
            Cleanup();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (_gate)
        {
            if (_raw is null || _capture is null) return;
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
        _capture?.StopRecording();
        ThreadPool.QueueUserWorkItem(_ => LimitReached?.Invoke(this, EventArgs.Empty));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null) CaptureFailed?.Invoke(this, args.Exception);
        _stopped.TrySetResult();
    }

    private void Cleanup()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
        }
        _capture = null;
        _raw?.Dispose();
        _raw = null;
        _format = null;
    }

    private static MMDevice? TryGetDefault(MMDeviceEnumerator enumerator)
    {
        try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return null; }
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() { Cancel(); lock (_gate) Cleanup(); }
}
