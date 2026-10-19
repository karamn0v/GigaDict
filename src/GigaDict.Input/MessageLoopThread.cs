namespace GigaDict.Input;

// A thread that only pumps Win32 messages for hooks installed on it. Hook
// callbacks are delivered through this loop, so UI work, modal dialogs and
// UI Automation on other threads cannot delay them. Setup and teardown run on
// the loop thread: UnhookWinEvent must be called from the installing thread.
internal sealed class MessageLoopThread : IDisposable
{
    private readonly Thread _thread;
    private uint _threadId;
    private int _disposed;

    // setup returns the matching teardown; an exception from setup is rethrown here.
    internal MessageLoopThread(string name, Func<Action> setup, ThreadPriority priority = ThreadPriority.Normal)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(setup, ready)) { IsBackground = true, Name = name, Priority = priority };
        _thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    private void Run(Func<Action> setup, TaskCompletionSource ready)
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        // Create the thread message queue before Dispose can post WM_QUIT to it.
        NativeMethods.PeekMessageW(out _, 0, 0, 0, NativeMethods.PmNoRemove);
        Action teardown;
        try { teardown = setup(); }
        catch (Exception exception) { ready.SetException(exception); return; }
        ready.SetResult();
        try
        {
            while (NativeMethods.GetMessageW(out var message, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessageW(in message);
            }
        }
        finally { teardown(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || !_thread.IsAlive) return;
        NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
