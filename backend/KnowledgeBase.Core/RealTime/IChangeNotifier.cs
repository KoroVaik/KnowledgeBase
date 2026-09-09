namespace KnowledgeBase.Core.RealTime;

/// <summary>
/// Fans changes out to whoever is currently listening. Publishers know nothing about how the
/// events reach a browser, or whether anyone is there at all.
/// </summary>
public interface IChangeNotifier
{
    void Publish(ChangeEvent change);

    ChangeSubscription Subscribe();
}
