namespace KnowledgeBase.Core.Persistence;

// Join row: note N carries tag T. Ordinal is the relevance order; position 0 is the
// "primary" tag by convention - no IsPrimary flag.
public sealed class NoteTag
{
    public required string NoteId { get; init; }

    public required string TagId { get; init; }

    public required int Ordinal { get; set; }
}
