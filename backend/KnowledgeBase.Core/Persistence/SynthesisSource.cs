namespace KnowledgeBase.Core.Persistence;

// Provenance edge: synthesis note S was built from note I (a Source note, or another
// Synthesis when S is the Index). Kept so a synthesis can later be found stale and rebuilt.
public sealed class SynthesisSource
{
    public required string SynthesisNoteId { get; init; }

    public required string InputNoteId { get; init; }
}
