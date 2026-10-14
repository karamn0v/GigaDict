namespace GigaDict.Core.Input;

public enum HotkeyAction { None, StartRecording, StopRecording, CancelRecording }

public sealed class HotkeyStateMachine(IReadOnlySet<int> combination, int escapeKey = 0x1B)
{
    private readonly HashSet<int> _pressed = [];
    private bool _recording;
    private bool _cancelledUntilReleased;

    public bool IsHeld => combination.All(_pressed.Contains);
    public bool IsRecording => _recording;

    public HotkeyAction KeyDown(int virtualKey, bool injectedByGigaDict)
    {
        if (injectedByGigaDict) return HotkeyAction.None;
        if (virtualKey == escapeKey && _recording)
        {
            _recording = false;
            _cancelledUntilReleased = true;
            return HotkeyAction.CancelRecording;
        }
        if (!_pressed.Add(virtualKey)) return HotkeyAction.None;
        if (!_recording && !_cancelledUntilReleased && combination.All(_pressed.Contains))
        {
            _recording = true;
            return HotkeyAction.StartRecording;
        }
        return HotkeyAction.None;
    }

    public HotkeyAction KeyUp(int virtualKey, bool injectedByGigaDict)
    {
        if (injectedByGigaDict) return HotkeyAction.None;
        var wasPart = combination.Contains(virtualKey);
        _pressed.Remove(virtualKey);
        if (_cancelledUntilReleased && !combination.Any(_pressed.Contains)) _cancelledUntilReleased = false;
        if (_recording && wasPart)
        {
            _recording = false;
            return HotkeyAction.StopRecording;
        }
        return HotkeyAction.None;
    }
}

