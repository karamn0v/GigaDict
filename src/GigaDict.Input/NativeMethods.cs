using System.Runtime.InteropServices;

namespace GigaDict.Input;

internal static partial class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmSysKeyUp = 0x0105;
    internal const uint LlkhfInjected = 0x10;
    internal const uint InputKeyboard = 1;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint KeyeventfUnicode = 0x0004;
    internal const uint EventObjectFocus = 0x8005;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventSystemDesktopSwitch = 0x0020;
    internal const uint WineventOutofcontext = 0;
    internal static readonly nuint InjectionMarker = unchecked((nuint)0x47444943544UL);

    internal delegate nint LowLevelKeyboardProc(int code, nuint wParam, nint lParam);
    internal delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct { internal uint VkCode; internal uint ScanCode; internal uint Flags; internal uint Time; internal nuint ExtraInfo; }

    // INPUT is 40 bytes on x64: DWORD type, 4 bytes alignment, then a 32-byte union.
    // KEYBDINPUT itself is only 24 bytes, therefore relying on its size makes
    // SendInput reject cbSize with ERROR_INVALID_PARAMETER (87).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct Input
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal KeybdInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeybdInput { internal ushort VirtualKey; internal ushort ScanCode; internal uint Flags; internal uint Time; internal nuint ExtraInfo; }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc callback, nint module, uint threadId);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] Input[] inputs, int size);
    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll")]
    internal static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hook);
}

