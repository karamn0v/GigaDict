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

    [Fact]
    public void PartialCombinationPreservesNormalShortcutsAndBalancedKeyEvents()
    {
        var state = new HotkeyStateMachine(new HashSet<int> { 0xA2, Space });
        Assert.False(state.ProcessKey(0xA2, down: true, injected: false).Suppress);
        Assert.False(state.ProcessKey('C', down: true, injected: false).Suppress);
        Assert.False(state.ProcessKey('C', down: false, injected: false).Suppress);
        var start = state.ProcessKey(Space, down: true, injected: false);
        Assert.Equal(HotkeyAction.StartRecording, start.Action);
        Assert.True(start.Suppress);
        Assert.True(state.ProcessKey(Space, down: true, injected: false).Suppress);
        var stop = state.ProcessKey(0xA2, down: false, injected: false);
        Assert.Equal(HotkeyAction.StopRecording, stop.Action);
        Assert.False(stop.Suppress);
        Assert.True(state.ProcessKey(Space, down: false, injected: false).Suppress);
        Assert.False(state.ProcessKey(Space, down: true, injected: false).Suppress);
        Assert.False(state.ProcessKey(Space, down: false, injected: false).Suppress);
    }

    [Fact]
    public void EscapeAndStandaloneHotkeySuppressBothDownAndUp()
    {
        var state = new HotkeyStateMachine(new HashSet<int> { 0x77 });
        Assert.True(state.ProcessKey(0x77, down: true, injected: false).Suppress);
        var cancel = state.ProcessKey(0x1B, down: true, injected: false);
        Assert.Equal(HotkeyAction.CancelRecording, cancel.Action);
        Assert.True(cancel.Suppress);
        Assert.True(state.ProcessKey(0x1B, down: true, injected: false).Suppress);
        Assert.True(state.ProcessKey(0x1B, down: false, injected: false).Suppress);
        Assert.True(state.ProcessKey(0x77, down: false, injected: false).Suppress);
        Assert.False(state.ProcessKey(0x1B, down: true, injected: false).Suppress);
    }

    [Fact]
    public void ModifierStillHeldAfterCombinationReleaseBlocksInsertion()
    {
        var state = new HotkeyStateMachine(new HashSet<int> { Ctrl, 0x10, Space });
        state.KeyDown(0xA2, false); state.KeyDown(0xA0, false); state.KeyDown(Space, false);
        state.KeyUp(Space, false);
        Assert.False(state.IsHeld);
        Assert.True(state.IsAnyPartPressed);
        state.KeyUp(0xA2, false); state.KeyUp(0xA0, false);
        Assert.False(state.IsAnyPartPressed);
    }

    [Fact]
    public void ResetForgetsKeyWhoseReleaseWasLost()
    {
        const int f8 = 0x77;
        var state = new HotkeyStateMachine(new HashSet<int> { f8 });
        Assert.Equal(HotkeyAction.StartRecording, state.ProcessKey(f8, down: true, injected: false).Action);
        // The key-up happened on the secure desktop and never reached the hook.
        state.Reset();
        Assert.False(state.IsAnyPartPressed);
        Assert.False(state.IsRecording);
        Assert.Equal(HotkeyAction.StartRecording, state.ProcessKey(f8, down: true, injected: false).Action);
        Assert.Equal(HotkeyAction.StopRecording, state.ProcessKey(f8, down: false, injected: false).Action);
    }

    [Theory]
    [InlineData(0xA2, 0xA0)]
    [InlineData(0xA3, 0xA1)]
    public void GenericModifiersMatchEitherPhysicalSide(int control, int shift)
    {
        var state = new HotkeyStateMachine(new HashSet<int> { Ctrl, 0x10, Space });
        Assert.False(state.ProcessKey(control, down: true, injected: false).Suppress);
        Assert.False(state.ProcessKey(shift, down: true, injected: false).Suppress);
        Assert.Equal(HotkeyAction.StartRecording, state.ProcessKey(Space, down: true, injected: false).Action);
        Assert.True(state.IsHeld);
        Assert.Equal(HotkeyAction.StopRecording, state.ProcessKey(control, down: false, injected: false).Action);
        Assert.False(state.IsHeld);
    }
}

