using System.Collections.Concurrent;

namespace KnowledgeBase.Core.RealTime;

public sealed class ChangeNotifier : IChangeNotifier
{
    // A concurrent set (the BCL has none): publish races subscribe/leave on request threads.
    private readonly ConcurrentDictionary<ChangeSubscription, byte> _subscriptions = new();

    public ChangeSubscription Subscribe()
    {
        var subscription = new ChangeSubscription(leaving => _subscriptions.TryRemove(leaving, out _));
        _subscriptions[subscription] = 0;

        return subscription;
    }

    // Never awaits, never throws: a slow or gone listener must not affect the upload.
    public void Publish(ChangeEvent change)
    {
        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Post(change);
        }
    }
}
