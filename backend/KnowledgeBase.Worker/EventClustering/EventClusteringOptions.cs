namespace KnowledgeBase.Worker.EventClustering;

public sealed class EventClusteringOptions
{
    public const string SectionName = "EventClustering";

    public double PairScoreThreshold { get; set; } = 0.55;
}
