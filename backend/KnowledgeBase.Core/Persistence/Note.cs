namespace KnowledgeBase.Core.Persistence;

public sealed class Note
{
    public required string Id { get; init; }

    public required NoteKind Kind { get; init; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    public string? SourceAssetId { get; init; }

    // Copied from the asset. SourceAssetId (the FK) is nulled when the file is deleted; this
    // stays, so the bin entry can still name the file.
    public string? SourceFileName { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; set; }

    // Soft delete: without it "deleted" and "never existed" look identical to a link here.
    public DateTime? DeletedAtUtc { get; set; }
}
