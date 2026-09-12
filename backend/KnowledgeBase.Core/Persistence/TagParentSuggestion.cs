namespace KnowledgeBase.Core.Persistence;

// An AI-proposed is-a link, not yet a real TagParent. Search runs one direction only (for a
// child with no parent yet); a tag's "suggested children" are the rows where it is ParentId -
// the flip side of another tag's own search. See "Tag hierarchy" in docs/database.md.
public sealed class TagParentSuggestion
{
    public required string ChildId { get; init; }

    public required string ParentId { get; init; }

    // The user rejected this guess - kept, not deleted, so the same pair is not proposed again
    // on the next suggest-hierarchy run.
    public required bool Dismissed { get; set; }
}
