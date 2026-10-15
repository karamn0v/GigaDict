using GigaDict.Core.Input;

namespace GigaDict.Tests;

public sealed class ClipboardInsertionTests
{
    [Fact]
    public async Task FocusLossAfterReplacementRestoresClipboardAndReportsFailure()
    {
        var safe = true;
        var clipboard = "previous";
        var pasted = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ClipboardInsertionTransaction.ExecuteAsync(
            () => safe,
            () => { clipboard = "replacement"; safe = false; return Task.CompletedTask; },
            () => pasted = true,
            () => { clipboard = "previous"; return Task.CompletedTask; }));
        Assert.False(pasted);
        Assert.Equal("previous", clipboard);
    }

    [Fact]
    public async Task SendPasteFailureStillRestoresClipboard()
    {
        var restored = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ClipboardInsertionTransaction.ExecuteAsync(
            () => true, () => Task.CompletedTask,
            () => throw new InvalidOperationException("input blocked"),
            () => { restored = true; return Task.CompletedTask; }));
        Assert.True(restored);
    }

    [Fact]
    public async Task CancellationDuringPasteDelayStillRestoresClipboard()
    {
        using var cancellation = new CancellationTokenSource();
        var restored = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClipboardInsertionTransaction.ExecuteAsync(
            () => true, () => Task.CompletedTask, cancellation.Cancel,
            () => { restored = true; return Task.CompletedTask; }, cancellation.Token));
        Assert.True(restored);
    }

    [Fact]
    public async Task UnsafeInitialFocusDoesNotTouchClipboard()
    {
        var replaced = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ClipboardInsertionTransaction.ExecuteAsync(
            () => false, () => { replaced = true; return Task.CompletedTask; },
            () => throw new InvalidOperationException("must not paste"), () => Task.CompletedTask));
        Assert.False(replaced);
    }
}
