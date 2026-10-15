using System.Runtime.InteropServices;
using GigaDict.Core.Configuration;
using GigaDict.Core.Input;
using GigaDict.Core.Tasks;
using Interop.UIAutomationClient;

namespace GigaDict.Input;

public sealed class FocusGuard : IFocusGuard, IDisposable
{
    private readonly FocusContinuityTracker _continuity = new();
    private readonly NativeMethods.WinEventProc? _callback;
    private readonly IUIAutomation? _automation;
    private nint _eventHook;
    private nint _foregroundHook;
    private nint _desktopHook;

    public FocusGuard()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            _automation = new CUIAutomation8Class();
            _callback = OnFocusChanged;
            _eventHook = NativeMethods.SetWinEventHook(NativeMethods.EventObjectFocus, NativeMethods.EventObjectFocus,
                0, _callback, 0, 0, NativeMethods.WineventOutofcontext);
            _foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground,
                0, _callback, 0, 0, NativeMethods.WineventOutofcontext);
            _desktopHook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemDesktopSwitch, NativeMethods.EventSystemDesktopSwitch,
                0, _callback, 0, 0, NativeMethods.WineventOutofcontext);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            _automation = null;
        }
    }

    public FocusIdentity? Capture()
    {
        if (_automation is null || _eventHook == 0 || _foregroundHook == 0 || _desktopHook == 0) return null;
        try
        {
            var window = NativeMethods.GetForegroundWindow();
            if (window == 0) return null;
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            var element = _automation.GetFocusedElement();
            if (element is null) return null;
            var runtimeId = element.GetRuntimeId();
            if (runtimeId is null || runtimeId.Length == 0) return null;
            var runtime = string.Join('.', runtimeId);
            var editable = element.CurrentIsEnabled != 0 && element.CurrentControlType is 50004 or 50030;
            return new(window, checked((int)processId), runtime, editable, element.CurrentIsPassword != 0);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return null;
        }
    }

    public void Track(Guid taskId, FocusIdentity identity, FocusProtectionMode mode = FocusProtectionMode.Strict) => _continuity.Track(taskId, identity, mode);
    public bool IsStillSafe(Guid taskId) => _continuity.IsStillSafe(taskId, Capture());
    public void Forget(Guid taskId) => _continuity.Forget(taskId);
    public void InvalidateAll() => _continuity.InvalidateAll();

    private void OnFocusChanged(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time)
    {
        _continuity.InvalidateAll();
    }

    private static bool IsRecoverable(Exception exception) => exception is
        COMException or InvalidCastException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException;

    public void Dispose()
    {
        if (_eventHook != 0) NativeMethods.UnhookWinEvent(_eventHook);
        if (_foregroundHook != 0) NativeMethods.UnhookWinEvent(_foregroundHook);
        if (_desktopHook != 0) NativeMethods.UnhookWinEvent(_desktopHook);
        _eventHook = 0;
        _foregroundHook = 0;
        _desktopHook = 0;
        if (_automation is not null && Marshal.IsComObject(_automation)) Marshal.FinalReleaseComObject(_automation);
    }
}
