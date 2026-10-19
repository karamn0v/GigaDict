using System.ComponentModel;
using System.Runtime.InteropServices;
using GigaDict.Core.Input;

namespace GigaDict.Input;

public static class UnicodeTextInserter
{
    public static void Insert(string text, Func<bool> focusIsSafe)
    {
        ArgumentNullException.ThrowIfNull(text);
        const int batchCharacters = 64;
        foreach (var batch in UnicodeBatches.Split(text, batchCharacters))
        {
            if (!focusIsSafe()) throw new InvalidOperationException("Фокус изменился во время вставки.");
            var (offset, length) = batch.GetOffsetAndLength(text.Length);
            var inputs = new NativeMethods.Input[length * 2];
            for (var index = 0; index < length; index++)
            {
                var codeUnit = text[offset + index];
                inputs[index * 2] = Create(codeUnit, false);
                inputs[index * 2 + 1] = Create(codeUnit, true);
            }
            var sent = NativeMethods.SendInput(checked((uint)inputs.Length), inputs, Marshal.SizeOf<NativeMethods.Input>());
            if (sent != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput ввёл не все символы.");
        }
    }

    private static NativeMethods.Input Create(char codeUnit, bool keyUp) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Keyboard = new NativeMethods.KeybdInput
        {
            ScanCode = codeUnit,
            Flags = NativeMethods.KeyeventfUnicode | (keyUp ? NativeMethods.KeyeventfKeyup : 0),
            ExtraInfo = NativeMethods.InjectionMarker
        }
    };
}
