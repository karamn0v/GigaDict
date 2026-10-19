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
            () => { restored = true; return Task.CompletedTask; }, cancellationToken: cancellation.Token));
        Assert.True(restored);
    }

    [Fact]
    public async Task RestoreWaitsForPasteToSettle()
    {
        var delay = TimeSpan.FromMilliseconds(200);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan? pastedAt = null, restoredAt = null;
        await ClipboardInsertionTransaction.ExecuteAsync(() => true, () => Task.CompletedTask,
            () => pastedAt = watch.Elapsed,
            () => { restoredAt = watch.Elapsed; return Task.CompletedTask; }, pasteSettleDelay: delay);
        Assert.NotNull(pastedAt);
        Assert.True(restoredAt - pastedAt >= delay - TimeSpan.FromMilliseconds(20));
        Assert.True(ClipboardInsertionTransaction.DefaultPasteSettleDelay > TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void SnapshotCopiesFormatsSoLaterClipboardChangesDoNotLeak()
    {
        // Simulates OLE's live clipboard object: reads always return current content.
        var live = new Dictionary<string, object> { ["UnicodeText"] = "user secret", ["HTML Format"] = "<b>x</b>" };
        var snapshot = ClipboardSnapshot.Capture(live.Keys.ToArray(), format => live[format]);
        live["UnicodeText"] = "dictated text";
        live.Remove("HTML Format");
        Assert.Equal("user secret", snapshot.Single(x => x.Key == "UnicodeText").Value);
        Assert.Equal("<b>x</b>", snapshot.Single(x => x.Key == "HTML Format").Value);
    }

    [Fact]
    public void SnapshotSkipsUnreadableAndEmptyFormats()
    {
        var snapshot = ClipboardSnapshot.Capture(["Broken", "Empty", "Text", "Text"], format => format switch
        {
            "Broken" => throw new InvalidOperationException("delayed rendering failed"),
            "Empty" => null,
            _ => "value"
        });
        Assert.Equal("Text", Assert.Single(snapshot).Key);
    }

    [Fact]
    public void PrivacyFormatsOptOutOfHistoryCloudAndMonitors()
    {
        var formats = ClipboardPrivacyFormats.Create().ToDictionary(x => x.Key, x => x.Value);
        Assert.True(formats.ContainsKey("ExcludeClipboardContentFromMonitorProcessing"));
        Assert.Equal(new byte[4], formats["CanIncludeInClipboardHistory"]);
        Assert.Equal(new byte[4], formats["CanUploadToCloudClipboard"]);
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
