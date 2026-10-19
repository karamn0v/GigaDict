namespace GigaDict.Core.Input;

public enum HotkeyAction { None, StartRecording, StopRecording, CancelRecording }
public readonly record struct HotkeyDecision(HotkeyAction Action, bool Suppress);

public sealed class HotkeyStateMachine(IReadOnlySet<int> combination, int escapeKey = 0x1B)
{
    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<int> _suppressed = [];
    private readonly object _gate = new();
    private bool _recording;
    private bool _cancelledUntilReleased;

    public bool IsHeld { get { lock (_gate) return IsCombinationHeld(); } }
    public bool IsRecording { get { lock (_gate) return _recording; } }
    // Insertion must wait for every key of the combination: a still-held modifier
    // would turn typed text or Ctrl+V into another shortcut.
    public bool IsAnyPartPressed { get { lock (_gate) return _pressed.Any(IsPart); } }

    // Key-up events are not delivered while the secure desktop or a locked session
    // is active. Forget the physical state so a stale key cannot block insertion.
    public void Reset()
    {
        lock (_gate)
        {
            _pressed.Clear();
            _suppressed.Clear();
            _recording = false;
            _cancelledUntilReleased = false;
        }
    }

    public HotkeyDecision ProcessKey(int virtualKey, bool down, bool injected)
    {
        lock (_gate)
        {
            if (injected) return new(HotkeyAction.None, false);
            var action = down ? KeyDownCore(virtualKey) : KeyUpCore(virtualKey);
            var suppress = down
                ? _suppressed.Contains(virtualKey) || action == HotkeyAction.CancelRecording ||
                    (_recording && IsPart(virtualKey) && (!IsModifier(virtualKey) || combination.Count == 1))
                : _suppressed.Remove(virtualKey);
            if (down && suppress) _suppressed.Add(virtualKey);
            return new(action, suppress);
        }
    }

    public HotkeyAction KeyDown(int virtualKey, bool injectedByGigaDict)
    {
        if (injectedByGigaDict) return HotkeyAction.None;
        lock (_gate) return KeyDownCore(virtualKey);
    }

    private HotkeyAction KeyDownCore(int virtualKey)
    {
        if (virtualKey == escapeKey && _recording)
        {
            _recording = false;
            _cancelledUntilReleased = true;
            return HotkeyAction.CancelRecording;
        }
        if (!_pressed.Add(virtualKey)) return HotkeyAction.None;
        if (!_recording && !_cancelledUntilReleased && IsCombinationHeld())
        {
            _recording = true;
            return HotkeyAction.StartRecording;
        }
        return HotkeyAction.None;
    }

    public HotkeyAction KeyUp(int virtualKey, bool injectedByGigaDict)
    {
        if (injectedByGigaDict) return HotkeyAction.None;
        lock (_gate) return KeyUpCore(virtualKey);
    }

    private HotkeyAction KeyUpCore(int virtualKey)
    {
        var wasPart = IsPart(virtualKey);
        _pressed.Remove(virtualKey);
        if (_cancelledUntilReleased && !_pressed.Any(IsPart)) _cancelledUntilReleased = false;
        if (_recording && wasPart)
        {
            _recording = false;
            return HotkeyAction.StopRecording;
        }
        return HotkeyAction.None;
    }

    private bool IsCombinationHeld() => combination.All(configured => _pressed.Any(actual => Matches(configured, actual)));
    private bool IsPart(int actual) => combination.Any(configured => Matches(configured, actual));
    private static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5;
    private static bool Matches(int configured, int actual) => configured == actual || configured switch
    {
        0x10 => actual is 0xA0 or 0xA1,
        0x11 => actual is 0xA2 or 0xA3,
        0x12 => actual is 0xA4 or 0xA5,
        _ => false
    };
}

