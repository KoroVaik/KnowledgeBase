namespace KnowledgeBase.Core.Persistence;

public sealed class Note
{
    public required string Id { get; init; }

    public required string Title { get; set; }

    public required string Category { get; set; }

    public required string Body { get; set; }

    public string? SourceAssetId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; set; }
}
