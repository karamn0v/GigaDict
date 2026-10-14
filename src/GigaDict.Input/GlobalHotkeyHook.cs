using System.ComponentModel;
using System.Runtime.InteropServices;
using GigaDict.Core.Input;

namespace GigaDict.Input;

public sealed class GlobalHotkeyHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly HotkeyStateMachine _state;
    private readonly HashSet<int> _combination;
    private nint _hook;

    public GlobalHotkeyHook(IEnumerable<int> virtualKeys)
    {
        _combination = virtualKeys.ToHashSet();
        if (_combination.Count == 0) throw new ArgumentException("Комбинация пуста.", nameof(virtualKeys));
        _state = new HotkeyStateMachine(_combination);
        _callback = HookCallback;
    }

    public event EventHandler<HotkeyAction>? Action;
    public bool IsHeld => _state.IsHeld;

    public void Install()
    {
        if (_hook != 0) return;
        _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WhKeyboardLl, _callback, 0, 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить keyboard hook.");
    }

    private nint HookCallback(int code, nuint message, nint parameter)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, message, parameter);
        var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(parameter);
        var injected = (data.Flags & NativeMethods.LlkhfInjected) != 0 || data.ExtraInfo == NativeMethods.InjectionMarker;
        var key = checked((int)data.VkCode);
        var down = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var up = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
        var action = down ? _state.KeyDown(key, injected) : up ? _state.KeyUp(key, injected) : HotkeyAction.None;
        if (action != HotkeyAction.None) ThreadPool.QueueUserWorkItem(_ => Action?.Invoke(this, action));
        var suppress = !injected && (_combination.Contains(key) || action == HotkeyAction.CancelRecording || (key == 0x1B && _state.IsRecording));
        return suppress ? 1 : NativeMethods.CallNextHookEx(_hook, code, message, parameter);
    }

    public void Dispose()
    {
        if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }
}

