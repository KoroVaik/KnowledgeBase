namespace KnowledgeBase.Core.Persistence;

public sealed class Note
{
    public required string Id { get; init; }

    public required NoteKind Kind { get; init; }

    public required string Title { get; set; }

    public required string Category { get; set; }

    public required string Body { get; set; }

    public string? SourceAssetId { get; init; }

    // Kept verbatim from the asset at creation time. SourceAssetId is a foreign key and gets
    // nulled when the file is deleted; this stays, so the entry left in the bin can still say
    // which file it came from.
    public string? SourceFileName { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; set; }

    // Set instead of deleting the row. Without it "the note was deleted" and "there never was
    // such a note" look identical to a link pointing here, and the reader cannot be told apart
    // which of the two happened.
    public DateTime? DeletedAtUtc { get; set; }
}
