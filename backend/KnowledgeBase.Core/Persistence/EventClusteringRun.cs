namespace KnowledgeBase.Core.Persistence;

public sealed class EventClusteringRun
{
    public required string Id { get; init; }
    public required string PipelineVersion { get; init; }
    public required string ModelKey { get; init; }
    public required string ConfigurationHash { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
}
