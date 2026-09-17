namespace KnowledgeBase.Core.Persistence;

public sealed class EventCluster
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required double Score { get; init; }
    public required string SignalsJson { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? SupersededAtUtc { get; set; }
}
