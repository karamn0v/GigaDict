namespace GigaDict.Core.Input;

public static class ClipboardInsertionTransaction
{
    // The target reads the clipboard asynchronously after Ctrl+V. Restoring too
    // early pastes the previous clipboard (possibly a password) instead of the text;
    // busy WebView2/Electron editors may need more than 150 ms.
    public static readonly TimeSpan DefaultPasteSettleDelay = TimeSpan.FromMilliseconds(500);

    public static async Task ExecuteAsync(Func<bool> focusIsSafe,
        Func<Task> replaceClipboard, Action sendPaste, Func<Task> restoreClipboard,
        TimeSpan? pasteSettleDelay = null, CancellationToken cancellationToken = default)
    {
        if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился перед вставкой.");
        try
        {
            await replaceClipboard().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился перед вставкой.");
            sendPaste();
            await Task.Delay(pasteSettleDelay ?? DefaultPasteSettleDelay, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            // Restoration must also run on focus loss, SendInput failure and shutdown.
            // The platform adapter checks ownership before touching the clipboard.
            await restoreClipboard().ConfigureAwait(true);
        }
    }
}
