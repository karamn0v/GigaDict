using GigaDict.Core.Queue;

namespace GigaDict.Tests;

public sealed class SequentialActionQueueTests
{
    [Fact]
    public async Task LaterActionsCannotOvertakeAnAwaitingAction()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        await using var queue = new SequentialActionQueue(exception => Task.FromException(exception));
        queue.Start();
        Assert.True(queue.TryPost(async () =>
        {
            order.Add(1);
            started.SetResult();
            await release.Task;
            order.Add(2);
        }));
        Assert.True(queue.TryPost(() => { order.Add(3); return Task.CompletedTask; }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1], order);
        release.SetResult();
        await queue.DisposeAsync();
        Assert.Equal([1, 2, 3], order);
        Assert.False(queue.TryPost(() => Task.CompletedTask));
    }

    [Fact]
    public async Task FailedActionDoesNotStopLaterCommands()
    {
        var errors = new List<Exception>();
        var completed = false;
        await using var queue = new SequentialActionQueue(exception => { errors.Add(exception); return Task.CompletedTask; });
        queue.Start();
        queue.TryPost(() => throw new InvalidOperationException("test"));
        queue.TryPost(() => { completed = true; return Task.CompletedTask; });
        await queue.DisposeAsync();
        Assert.True(completed);
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
    }
}
