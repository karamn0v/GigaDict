using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GigaDict.Input;

public static class ClipboardTextInserter
{
    private const string OwnershipFormat = "GigaDict.ClipboardOwner";

    public static async Task InsertAsync(string text, Func<bool> focusIsSafe, CancellationToken cancellationToken = default)
    {
        if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился перед вставкой.");
        var previous = await RetryClipboardAsync(Clipboard.GetDataObject, cancellationToken).ConfigureAwait(true);
        var token = Guid.NewGuid().ToString("N");
        var replacement = new DataObject();
        replacement.SetText(text, TextDataFormat.UnicodeText);
        replacement.SetData(OwnershipFormat, token);
        await RetryClipboardAsync(() => { Clipboard.SetDataObject(replacement, true); return true; }, cancellationToken).ConfigureAwait(true);
        if (!focusIsSafe()) return;
        SendPaste();
        await Task.Delay(150, cancellationToken).ConfigureAwait(true);
        try
        {
            var current = Clipboard.GetDataObject();
            if (current?.GetData(OwnershipFormat) as string == token && previous is not null)
                Clipboard.SetDataObject(previous, true);
        }
        catch (ExternalException) { /* Чужой новый Clipboard нельзя перезаписывать. */ }
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
