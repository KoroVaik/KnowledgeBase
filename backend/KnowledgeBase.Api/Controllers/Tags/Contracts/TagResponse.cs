namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>
/// One tag in the vocabulary, with how many live Source notes carry it, for a
/// pipeline-invented tag the other tag in the vocabulary the model judged closest in meaning
/// (confirmed or itself still unconfirmed) plus how sure it was, and its direct parent tags
/// (is-a). A tag's direct children are every other tag whose
/// <c>ParentIds</c> includes it - not carried here to avoid an all-pairs field.
/// <c>HasPendingPlacementSuggestion</c> is true when this tag is on either side of a
/// non-dismissed <c>TagParentSuggestion</c> - the "To place" section fetches
/// <c>GET /api/tags/{id}/parent-suggestions</c> only for these. <c>PendingParentSuggestions</c>
/// carries this tag's own side of that (candidate parents for it, as the child), so the review
/// row can offer "confirm as a child of X" without a separate fetch.
/// </summary>
public sealed record TagResponse(
    string Id,
    string Name,
    bool Confirmed,
    int NoteCount,
    string? SuggestedMergeIntoId,
    string? SuggestedMergeConfidence,
    IReadOnlyList<string> ParentIds,
    bool HasPendingPlacementSuggestion,
    IReadOnlyList<TagParentSuggestionRef> PendingParentSuggestions);
