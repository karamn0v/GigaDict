using GigaDict.Core.Threading;

namespace GigaDict.Tests;

public sealed class DedicatedWorkerTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

    [Fact]
    public void RunsOnItsOwnNamedThread()
    {
        using var worker = new DedicatedWorker("uia-test");
        var caller = Environment.CurrentManagedThreadId;
        Assert.True(worker.TryInvoke(() => (Thread.CurrentThread.Name, Environment.CurrentManagedThreadId), Long, out var result));
        Assert.Equal("uia-test", result.Name);
        Assert.NotEqual(caller, result.CurrentManagedThreadId);
    }

    [Fact]
    public void ExceptionReachesCaller()
    {
        using var worker = new DedicatedWorker("uia-test");
        Assert.Throws<InvalidOperationException>(() =>
            worker.TryInvoke<int>(() => throw new InvalidOperationException("uia"), Long, out _));
    }

    [Fact]
    public async Task HungCallTimesOutCallersWithoutRunningAbandonedWork()
    {
        using var worker = new DedicatedWorker("uia-test");
        using var release = new ManualResetEventSlim();
        var hung = Task.Run(() => worker.TryInvoke(() => release.Wait(Long), TimeSpan.FromMilliseconds(50), out _));
        Assert.False(await hung.WaitAsync(Long));

        var abandonedRan = false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(worker.TryInvoke(() => abandonedRan = true, TimeSpan.FromMilliseconds(100), out _));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));

        release.Set();
        Assert.True(worker.TryInvoke(() => 42, Long, out var value));
        Assert.Equal(42, value);
        Assert.False(abandonedRan);
    }

    [Fact]
    public void DisposedWorkerRefusesCalls()
    {
        var worker = new DedicatedWorker("uia-test");
        worker.Dispose();
        Assert.False(worker.TryInvoke(() => 1, Long, out _));
        worker.Dispose();
    }
}
