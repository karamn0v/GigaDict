using System.Collections.Concurrent;

namespace GigaDict.Core.Threading;

// Runs calls on one dedicated thread; callers stop waiting after a timeout.
// A hung call keeps the thread busy, so later calls time out as well instead of
// blocking their callers. Calls abandoned before they start are never run.
public sealed class DedicatedWorker : IDisposable
{
    private readonly BlockingCollection<IWorkItem> _work = new();
    private readonly Thread _thread;
    private int _disposed;

    public DedicatedWorker(string name, ApartmentState apartment = ApartmentState.MTA)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(apartment);
        _thread.Start();
    }

    public bool TryInvoke<T>(Func<T> function, TimeSpan timeout, out T result)
    {
        ArgumentNullException.ThrowIfNull(function);
        result = default!;
        var item = new WorkItem<T>(function);
        try { _work.Add(item); }
        catch (InvalidOperationException) { return false; } // completed or disposed
        bool finished;
        try { finished = item.Completion.Wait(timeout); }
        catch (AggregateException) { finished = true; } // rethrown unwrapped below
        if (!finished && item.TryAbandon()) return false;
        if (!item.Completion.IsCompleted) return false;
        result = item.Completion.GetAwaiter().GetResult();
        return true;
    }

    private void Run()
    {
        foreach (var item in _work.GetConsumingEnumerable()) item.Execute();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _work.CompleteAdding();
        // A hung call may never return; the background thread is then left behind.
        if (_thread.Join(TimeSpan.FromSeconds(2))) _work.Dispose();
    }

    private interface IWorkItem { void Execute(); }

    private sealed class WorkItem<T>(Func<T> function) : IWorkItem
    {
        private const int Pending = 0, Running = 1, Abandoned = 2;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state;

        public Task<T> Completion => _completion.Task;

        public bool TryAbandon() => Interlocked.CompareExchange(ref _state, Abandoned, Pending) == Pending;

        public void Execute()
        {
            if (Interlocked.CompareExchange(ref _state, Running, Pending) != Pending) return;
            try { _completion.SetResult(function()); }
            catch (Exception exception) { _completion.SetException(exception); }
        }
    }
}
