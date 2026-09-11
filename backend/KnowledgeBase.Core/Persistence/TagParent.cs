namespace KnowledgeBase.Core.Persistence;

// Directed is-a link: ChildId -> ParentId (Porsche -> Cars). A DAG, not a tree - a tag
// may have several parents. See "Tag hierarchy" in docs/database.md.
public sealed class TagParent
{
    public required string ChildId { get; init; }

    public required string ParentId { get; init; }
}
