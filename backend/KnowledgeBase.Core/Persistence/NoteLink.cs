namespace KnowledgeBase.Core.Persistence;

public sealed class NoteLink
{
    public required string Id { get; init; }

    public required string SourceNoteId { get; init; }

    // The raw [[target]] text, always stored. A link can point at a note that does not exist
    // yet; TargetNoteId stays null until one with a matching title turns up.
    public required string TargetTitle { get; set; }

    public string? TargetNoteId { get; set; }
}
