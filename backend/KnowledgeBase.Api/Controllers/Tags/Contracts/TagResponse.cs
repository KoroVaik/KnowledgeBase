namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>
/// One tag in the vocabulary, with how many live Source notes carry it, for a
/// pipeline-invented tag the other tag in the vocabulary the model judged closest in meaning
/// (confirmed or itself still unconfirmed), and its direct parent tags (is-a). A tag's direct
/// children are every other tag whose
/// <c>ParentIds</c> includes it - not carried here to avoid an all-pairs field.
/// <c>HasPendingPlacementSuggestion</c> is true when this tag is on either side of a
/// non-dismissed <c>TagParentSuggestion</c> - the "To place" section fetches
/// <c>GET /api/tags/{id}/parent-suggestions</c> only for these.
/// </summary>
public sealed record TagResponse(
    string Id,
    string Name,
    bool Confirmed,
    int NoteCount,
    string? SuggestedMergeIntoId,
    IReadOnlyList<string> ParentIds,
    bool HasPendingPlacementSuggestion);
