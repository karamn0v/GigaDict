using GigaDict.Core.Input;

namespace GigaDict.Tests;

public sealed class HotkeyStateMachineTests
{
    private const int Ctrl = 0x11;
    private const int Space = 0x20;

    [Fact]
    public void FullCombinationStartsAndAnyReleaseStops()
    {
        var state = new HotkeyStateMachine(new HashSet<int> { Ctrl, Space });
        Assert.Equal(HotkeyAction.None, state.KeyDown(Ctrl, false));
        Assert.Equal(HotkeyAction.StartRecording, state.KeyDown(Space, false));
        Assert.Equal(HotkeyAction.None, state.KeyDown(Space, false));
        Assert.Equal(HotkeyAction.StopRecording, state.KeyUp(Ctrl, false));
    }

    [Fact]
    public void EscapeCancelsUntilWholeCombinationReleased()
    {
        var state = new HotkeyStateMachine(new HashSet<int> { Ctrl, Space });
        state.KeyDown(Ctrl, false); state.KeyDown(Space, false);
        Assert.Equal(HotkeyAction.CancelRecording, state.KeyDown(0x1B, false));
        state.KeyUp(Space, false);
        Assert.Equal(HotkeyAction.None, state.KeyDown(Space, false));
        state.KeyUp(Space, false); state.KeyUp(Ctrl, false);
        state.KeyDown(Ctrl, false);
        Assert.Equal(HotkeyAction.StartRecording, state.KeyDown(Space, false));
    }

    [Fact]
    public void OwnInjectedEventsAreIgnored()
    {
        var state = new HotkeyStateMachine(new HashSet<int> { Space });
        Assert.Equal(HotkeyAction.None, state.KeyDown(Space, true));
        Assert.False(state.IsRecording);
    }
}

