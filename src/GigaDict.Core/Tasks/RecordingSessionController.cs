using GigaDict.Core.Configuration;
using GigaDict.Core.Input;

namespace GigaDict.Core.Tasks;

public interface IAudioRecorder
{
    void Start(string? deviceId, TimeSpan maximumDuration, long maximumPcmBytes);
    Task<byte[]> StopAsync(CancellationToken cancellationToken = default);
    void Cancel();
}

// All operations are called by the application's sequential action queue.
public sealed class RecordingSessionController(IAudioRecorder recorder, IFocusGuard focus)
{
    // 0.3 s of PCM16 mono 16 kHz. Accidental taps are dropped locally instead of
    // occupying the single NAS slot and ending with "empty result".
    public const int MinimumAudioBytes = 9600;

    private Session? _current;
    private long _sequence;

    public bool IsRecording => _current is not null;

    public void Start(AppSettings settings)
    {
        if (_current is not null) return;
        var session = new Session(Guid.NewGuid(), focus.Capture(), settings);
        if (session.Focus is not null) focus.Track(session.Id, session.Focus, settings.FocusProtection);
        _current = session;
        try
        {
            var taskBytes = Math.Min(settings.MaximumAudioMemoryMiB * 1024L * 1024L, 32000L * settings.MaximumRecordingSeconds);
            recorder.Start(settings.CaptureDeviceId, TimeSpan.FromSeconds(settings.MaximumRecordingSeconds), taskBytes);
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    public async Task<DictationTask?> CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_current is not { } session) return null;
        try
        {
            var audio = await recorder.StopAsync(cancellationToken).ConfigureAwait(false);
            if (audio.Length < MinimumAudioBytes)
            {
                Array.Clear(audio);
                focus.Forget(session.Id);
                return null;
            }
            var item = new DictationTask(++_sequence, audio, session.Focus, session.Settings, session.Id);
            if (!focus.IsStillSafe(session.Id)) item.InvalidateInsertion();
            return item;
        }
        catch
        {
            try { recorder.Cancel(); }
            finally { focus.Forget(session.Id); }
            throw;
        }
        finally { _current = null; }
    }

    public void Cancel()
    {
        if (_current is not { } session) return;
        try { recorder.Cancel(); }
        finally { focus.Forget(session.Id); _current = null; }
    }

    private sealed record Session(Guid Id, FocusIdentity? Focus, AppSettings Settings);
}
