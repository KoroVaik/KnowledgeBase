namespace KnowledgeBase.Core.Persistence;

// An AI-proposed is-a link, not yet a real TagParent. Search runs one direction only (for a
// child with no parent yet); a tag's "suggested children" are the rows where it is ParentId -
// the flip side of another tag's own search. See "Tag hierarchy" in docs/database.md.
public sealed class TagParentSuggestion
{
    public required string ChildId { get; init; }

    public required string ParentId { get; init; }

    // The user rejected this guess - kept, not deleted, and hidden until either the same pair is
    // revived (DeclineCount below the cap) or it is dismissed for good.
    public required bool Dismissed { get; set; }

    // How many times the user has rejected this exact pair. TagHierarchyHandler revives a
    // dismissed row (clears Dismissed) when the model proposes it again and this is still below
    // its cap; past the cap the pair is never proposed again.
    public required int DeclineCount { get; set; }

    // How sure the model was about this placement - see SuggestionConfidence.
    public required SuggestionConfidence Confidence { get; set; }
}
