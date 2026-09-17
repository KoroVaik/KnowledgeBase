namespace KnowledgeBase.Core.Persistence;

public sealed class Location
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required LocationKind Kind { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
}
