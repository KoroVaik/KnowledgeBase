namespace KnowledgeBase.Core.Pipeline;

public interface IWorkerJobNotifier
{
    void NotifyJobQueued();
}

public sealed class NullWorkerJobNotifier : IWorkerJobNotifier
{
    public static readonly NullWorkerJobNotifier Instance = new();

    public void NotifyJobQueued()
    {
    }
}
