using System.Threading.Channels;

namespace GigaDict.Core.Queue;

// Producers only enqueue. A single consumer awaits each action before starting
// the next, including across asynchronous recording shutdown and resampling.
public sealed class SequentialActionQueue(Func<Exception, Task> onError) : IAsyncDisposable
{
    private readonly Channel<Func<Task>> _actions = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private Task? _worker;

    public void Start() => _worker ??= Task.Run(ProcessAsync);

    public bool TryPost(Func<Task> action) => _actions.Writer.TryWrite(action);

    private async Task ProcessAsync()
    {
        await foreach (var action in _actions.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception exception) { await onError(exception).ConfigureAwait(false); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _actions.Writer.TryComplete();
        if (_worker is not null) await _worker.ConfigureAwait(false);
    }
}
