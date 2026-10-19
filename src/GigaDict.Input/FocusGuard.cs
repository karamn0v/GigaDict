using System.ComponentModel;
using System.Runtime.InteropServices;
using GigaDict.Core.Configuration;
using GigaDict.Core.Input;
using GigaDict.Core.Tasks;
using GigaDict.Core.Threading;
using Interop.UIAutomationClient;

namespace GigaDict.Input;

public sealed class FocusGuard : IFocusGuard, IDisposable
{
    // UI Automation may block on an unresponsive target. A slow answer counts as
    // unknown focus, which forbids insertion.
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromMilliseconds(500);
    private const uint AutomationTimeoutMilliseconds = 1000;

    private readonly FocusContinuityTracker _continuity = new();
    private readonly NativeMethods.WinEventProc _callback;
    private readonly MessageLoopThread? _events;
    private readonly DedicatedWorker? _automationThread;
    private IUIAutomation? _automation; // used only on _automationThread

    public FocusGuard()
    {
        _callback = OnFocusChanged;
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // WinEvents are delivered through the installing thread's message loop.
            // A dedicated loop keeps invalidation timely even while the UI thread
            // is busy inserting text.
            _events = new MessageLoopThread("GigaDict focus events", InstallEventHooks);
        }
        catch (Win32Exception) { return; }
        _automationThread = new DedicatedWorker("GigaDict UI Automation", ApartmentState.MTA);
        // On failure _automation stays null and every capture reports unknown focus.
        _ = _automationThread.TryInvoke(CreateAutomation, TimeSpan.FromSeconds(5), out _);
    }

    public FocusIdentity? Capture()
    {
        if (_events is null || _automationThread is null) return null;
        return _automationThread.TryInvoke(CaptureCore, CaptureTimeout, out var identity) ? identity : null;
    }

    public void Track(Guid taskId, FocusIdentity identity, FocusProtectionMode mode = FocusProtectionMode.Strict) => _continuity.Track(taskId, identity, mode);
    public bool IsStillSafe(Guid taskId) => _continuity.IsStillSafe(taskId, Capture());
    public void Forget(Guid taskId) => _continuity.Forget(taskId);
    public void InvalidateAll() => _continuity.InvalidateAll();

    private Action InstallEventHooks()
    {
        var hooks = new List<nint>();
        void Unhook() { foreach (var hook in hooks) NativeMethods.UnhookWinEvent(hook); }
        foreach (var eventType in new[] { NativeMethods.EventObjectFocus, NativeMethods.EventSystemForeground, NativeMethods.EventSystemDesktopSwitch })
        {
            var hook = NativeMethods.SetWinEventHook(eventType, eventType, 0, _callback, 0, 0, NativeMethods.WineventOutofcontext);
            if (hook == 0)
            {
                // Without every hook a focus change could go unnoticed: refuse capture.
                Unhook();
                throw new Win32Exception("Не удалось установить WinEvent hook.");
            }
            hooks.Add(hook);
        }
        return Unhook;
    }

    private bool CreateAutomation()
    {
        try
        {
            var automation = new CUIAutomation8Class();
            if (automation is IUIAutomation2 bounded)
            {
                bounded.ConnectionTimeout = AutomationTimeoutMilliseconds;
                bounded.TransactionTimeout = AutomationTimeoutMilliseconds;
            }
            _automation = automation;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
    }

    private FocusIdentity? CaptureCore()
    {
        if (_automation is null) return null;
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

    private void OnFocusChanged(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time)
    {
        _continuity.InvalidateAll();
    }

    private static bool IsRecoverable(Exception exception) => exception is
        COMException or InvalidCastException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or TimeoutException;

    public void Dispose()
    {
        _events?.Dispose();
        if (_automationThread is null) return;
        _automationThread.TryInvoke(() =>
        {
            if (_automation is not null && Marshal.IsComObject(_automation)) Marshal.FinalReleaseComObject(_automation);
            _automation = null;
            return true;
        }, TimeSpan.FromSeconds(2), out _);
        _automationThread.Dispose();
    }
}
