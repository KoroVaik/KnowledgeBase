namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>
/// One tag in the vocabulary, with how many live Source notes carry it, for a
/// pipeline-invented tag the confirmed tag the model judged closest in meaning, and its
/// direct parent tags (is-a). A tag's direct children are every other tag whose
/// <c>ParentIds</c> includes it - not carried here to avoid an all-pairs field.
/// </summary>
public sealed record TagResponse(
    string Id,
    string Name,
    bool Confirmed,
    int NoteCount,
    string? SuggestedMergeIntoId,
    IReadOnlyList<string> ParentIds);
