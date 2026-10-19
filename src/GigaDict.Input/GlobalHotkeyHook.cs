using System.ComponentModel;
using System.Runtime.InteropServices;
using GigaDict.Core.Input;

namespace GigaDict.Input;

public sealed class GlobalHotkeyHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly HotkeyStateMachine _state;
    private readonly HashSet<int> _combination;
    private MessageLoopThread? _thread;
    private nint _hook;

    public GlobalHotkeyHook(IEnumerable<int> virtualKeys)
    {
        _combination = virtualKeys.ToHashSet();
        if (_combination.Count == 0) throw new ArgumentException("Комбинация пуста.", nameof(virtualKeys));
        _state = new HotkeyStateMachine(_combination);
        _callback = HookCallback;
    }

    public event EventHandler<HotkeyAction>? Action;
    public bool IsAnyKeyHeld => _state.IsAnyPartPressed;
    public void Reset() => _state.Reset();

    public void Install()
    {
        if (_thread is not null) return;
        // Windows silently removes a low-level hook whose thread does not answer
        // within LowLevelHooksTimeout. The hook owns a thread that does nothing else.
        _thread = new MessageLoopThread("GigaDict keyboard hook", () =>
        {
            _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WhKeyboardLl, _callback, 0, 0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить keyboard hook.");
            return () => { NativeMethods.UnhookWindowsHookEx(_hook); _hook = 0; };
        }, ThreadPriority.AboveNormal);
    }

    private nint HookCallback(int code, nuint message, nint parameter)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, message, parameter);
        var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(parameter);
        var injected = (data.Flags & NativeMethods.LlkhfInjected) != 0 || data.ExtraInfo == NativeMethods.InjectionMarker;
        var key = checked((int)data.VkCode);
        var down = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var up = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
        if (!down && !up) return NativeMethods.CallNextHookEx(_hook, code, message, parameter);
        var decision = _state.ProcessKey(key, down, injected);
        // The subscriber only enqueues into the application's sequential worker.
        // Keep notification order and do not perform recording/UIA work here.
        if (decision.Action != HotkeyAction.None) Action?.Invoke(this, decision.Action);
        return decision.Suppress ? 1 : NativeMethods.CallNextHookEx(_hook, code, message, parameter);
    }

    public void Dispose()
    {
        _thread?.Dispose();
        _thread = null;
    }
}

