using System.Collections.Concurrent;

namespace KnowledgeBase.Core.RealTime;

public sealed class ChangeNotifier : IChangeNotifier
{
    // A set, spelled as a dictionary because there is no concurrent set in the BCL. Publishing
    // happens on a request thread while another request subscribes or leaves, so plain
    // HashSet + lock would be the alternative.
    private readonly ConcurrentDictionary<ChangeSubscription, byte> _subscriptions = new();

    public ChangeSubscription Subscribe()
    {
        var subscription = new ChangeSubscription(leaving => _subscriptions.TryRemove(leaving, out _));
        _subscriptions[subscription] = 0;

        return subscription;
    }

    // Never awaits and never throws: an upload must not slow down or fail because a listener
    // is slow or has just gone away.
    public void Publish(ChangeEvent change)
    {
        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Post(change);
        }
    }
}
