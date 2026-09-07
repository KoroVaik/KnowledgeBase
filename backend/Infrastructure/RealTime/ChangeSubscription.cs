using System.Threading.Channels;

namespace Backend.Infrastructure.RealTime;

/// <summary>
/// One listener's queue of pending changes. Disposing detaches it from the notifier.
/// </summary>
public sealed class ChangeSubscription : IDisposable
{
    // Bounded, and the oldest entry goes first when it overflows: a browser that stopped
    // reading - a phone that fell asleep mid-stream - must never hold up the upload that
    // published the event. Events are only "re-read the list" hints, so losing the older
    // ones costs nothing.
    private readonly Channel<ChangeEvent> _pending = Channel.CreateBounded<ChangeEvent>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly Action<ChangeSubscription> _detach;

    internal ChangeSubscription(Action<ChangeSubscription> detach) => _detach = detach;

    public ChannelReader<ChangeEvent> Reader => _pending.Reader;

    internal void Post(ChangeEvent change) => _pending.Writer.TryWrite(change);

    public void Dispose()
    {
        _detach(this);
        _pending.Writer.TryComplete();
    }
}
