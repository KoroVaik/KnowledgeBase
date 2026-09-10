namespace KnowledgeBase.Core.Persistence;

public sealed class Note
{
    public required string Id { get; init; }

    public required string Title { get; set; }

    public required string Category { get; set; }

    public required string Body { get; set; }

    public string? SourceAssetId { get; init; }

    // Kept verbatim from the asset at creation time. SourceAssetId is a foreign key and gets
    // nulled when the file is deleted; this stays, so the note can still say which file it
    // came from ("Related file <name> was removed").
    public string? SourceFileName { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; set; }
}
