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
    private const int TextPatternId = 10014; // UIA_TextPatternId

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

    // Characters around the caret of the focused element, only if that element
    // still satisfies the task's focus binding. Identity and context come from one
    // UIA call so they describe the same element. Null: no reliable context.
    public CaretContext? ReadCaretContext(Guid taskId)
    {
        if (_events is null || _automationThread is null) return null;
        if (!_automationThread.TryInvoke(ReadCaretCore, CaptureTimeout, out var read) || read.Identity is null) return null;
        return _continuity.IsStillSafe(taskId, read.Identity) ? read.Context : null;
    }
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

    private FocusIdentity? CaptureCore() => CaptureCore(out _);

    private FocusIdentity? CaptureCore(out IUIAutomationElement? element)
    {
        element = null;
        if (_automation is null) return null;
        try
        {
            var window = NativeMethods.GetForegroundWindow();
            if (window == 0) return null;
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            element = _automation.GetFocusedElement();
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

    private (FocusIdentity? Identity, CaretContext? Context) ReadCaretCore()
    {
        var identity = CaptureCore(out var element);
        if (identity is null || element is null || identity.IsPassword) return (identity, null);
        try
        {
            if (element.GetCurrentPattern(TextPatternId) is not IUIAutomationTextPattern pattern) return (identity, null);
            var selection = pattern.GetSelection();
            if (selection is null || selection.Length != 1) return (identity, null);
            var range = selection.GetElement(0);
            // A selection is replaced by the text, so its outer neighbours matter.
            return (identity, new CaretContext(ReadAdjacent(range, before: true), ReadAdjacent(range, before: false)));
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return (identity, null);
        }
    }

    private static char? ReadAdjacent(IUIAutomationTextRange selection, bool before)
    {
        var range = selection.Clone();
        var edge = before ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start : TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
        var opposite = before ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_End : TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
        // Collapse to the selection edge, then extend one character outward.
        range.MoveEndpointByRange(opposite, range, edge);
        if (range.MoveEndpointByUnit(edge, TextUnit.TextUnit_Character, before ? -1 : 1) == 0) return null;
        var text = range.GetText(-1);
        return string.IsNullOrEmpty(text) ? null : before ? text[^1] : text[0];
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
