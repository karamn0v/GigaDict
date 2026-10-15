using GigaDict.Core.Configuration;
using GigaDict.Core.Tasks;

namespace GigaDict.Core.Input;

public sealed class FocusContinuityTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, TrackedFocus> _tracked = [];
    private readonly HashSet<Guid> _invalidated = [];
    private long _generation;

    public void Track(Guid taskId, FocusIdentity identity, FocusProtectionMode mode = FocusProtectionMode.Strict)
    {
        lock (_gate) _tracked[taskId] = new(identity, mode, _generation);
    }

    public void Observe(FocusIdentity? current)
    {
        lock (_gate)
            foreach (var pair in _tracked)
                if (!Matches(pair.Value, current)) _invalidated.Add(pair.Key);
    }

    // WinEvent does not provide a trustworthy UIA identity. Invalidate at event
    // delivery, before deferred work could observe a return to the original field.
    public void InvalidateAll()
    {
        lock (_gate) _generation++;
    }

    public bool IsStillSafe(Guid taskId, FocusIdentity? current)
    {
        lock (_gate)
            return !_invalidated.Contains(taskId) && _tracked.TryGetValue(taskId, out var expected) &&
                expected.Generation == _generation && Matches(expected, current);
    }

    public void Forget(Guid taskId)
    {
        lock (_gate)
        {
            _tracked.Remove(taskId);
            _invalidated.Remove(taskId);
        }
    }

    private static bool Matches(TrackedFocus tracked, FocusIdentity? current)
    {
        if (current is null || tracked.Identity.IsPassword || current.IsPassword) return false;
        if (tracked.Mode == FocusProtectionMode.Strict) return Equals(tracked.Identity, current);
        return tracked.Identity.WindowHandle == current.WindowHandle && tracked.Identity.ProcessId == current.ProcessId;
    }

    private sealed record TrackedFocus(FocusIdentity Identity, FocusProtectionMode Mode, long Generation);
}

