using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GigaDict.Core.Input;

namespace GigaDict.Input;

public static class ClipboardTextInserter
{
    private const string OwnershipFormat = "GigaDict.ClipboardOwner";

    public static async Task InsertAsync(string text, Func<bool> focusIsSafe, CancellationToken cancellationToken = default)
    {
        if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился перед вставкой.");
        var previous = await RetryClipboardAsync(CaptureClipboard, cancellationToken).ConfigureAwait(true);
        var token = Guid.NewGuid().ToString("N");
        var replacement = CreatePrivateText(text);
        replacement.SetData(OwnershipFormat, token);
        await ClipboardInsertionTransaction.ExecuteAsync(focusIsSafe,
            () => RetryClipboardAsync(() => { Clipboard.SetDataObject(replacement, true); return true; }, cancellationToken),
            SendPaste,
            async () =>
            {
                try
                {
                    await RetryClipboardAsync(() =>
                    {
                        var current = Clipboard.GetDataObject();
                        if (current?.GetData(OwnershipFormat) as string == token)
                        {
                            if (previous is not null) Clipboard.SetDataObject(previous, true);
                            else Clipboard.Clear();
                        }
                        return true;
                    }, CancellationToken.None).ConfigureAwait(true);
                }
                catch (ExternalException) { /* A busy clipboard or another owner's data must not be overwritten. */ }
            }, cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    public static DataObject CreatePrivateText(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        foreach (var (format, value) in ClipboardPrivacyFormats.Create()) data.SetData(format, false, new MemoryStream(value));
        return data;
    }

    private static DataObject? CaptureClipboard()
    {
        var current = Clipboard.GetDataObject();
        if (current is null) return null;
        var snapshot = ClipboardSnapshot.Capture(current.GetFormats(false), format => current.GetData(format, false));
        if (snapshot.Count == 0) return null;
        var copy = new DataObject();
        foreach (var (format, value) in snapshot) copy.SetData(format, false, value);
        return copy;
    }

    private static async Task<T> RetryClipboardAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return operation(); }
            catch (ExternalException) when (attempt < 5)
            {
                await Task.Delay(25 * (attempt + 1), cancellationToken).ConfigureAwait(true);
            }
        }
    }

    private static void SendPaste()
    {
        const ushort control = 0x11;
        const ushort v = 0x56;
        var inputs = new[] { Key(control, false), Key(v, false), Key(v, true), Key(control, true) };
        var sent = NativeMethods.SendInput(checked((uint)inputs.Length), inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отправить Ctrl+V.");
    }

    private static NativeMethods.Input Key(ushort virtualKey, bool up) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Keyboard = new NativeMethods.KeybdInput
        {
            VirtualKey = virtualKey,
            Flags = up ? NativeMethods.KeyeventfKeyup : 0,
            ExtraInfo = NativeMethods.InjectionMarker
        }
    };
}
