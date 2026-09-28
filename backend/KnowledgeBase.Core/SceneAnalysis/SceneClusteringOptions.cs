namespace KnowledgeBase.Core.SceneAnalysis;

public sealed class SceneClusteringOptions
{
    public const string SectionName = "SceneClustering";

    public double LocationJoinThreshold { get; set; } = 0.70;
    public double ClusterThreshold { get; set; } = 0.65;
    public double HintThreshold { get; set; } = 0.50;
}
