namespace GigaDict.Core.Input;

public static class ClipboardInsertionTransaction
{
    public static async Task ExecuteAsync(Func<bool> focusIsSafe,
        Func<Task> replaceClipboard, Action sendPaste, Func<Task> restoreClipboard,
        CancellationToken cancellationToken = default)
    {
        if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился перед вставкой.");
        try
        {
            await replaceClipboard().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился перед вставкой.");
            sendPaste();
            await Task.Delay(150, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            // Restoration must also run on focus loss, SendInput failure and shutdown.
            // The platform adapter checks ownership before touching the clipboard.
            await restoreClipboard().ConfigureAwait(true);
        }
    }
}
