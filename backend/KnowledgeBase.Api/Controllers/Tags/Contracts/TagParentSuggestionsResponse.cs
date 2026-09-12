namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>One tag named in a placement suggestion - just enough for the suggestion graph.</summary>
public sealed record TagSuggestionResponse(string Id, string Name);

/// <summary>
/// Pending placement suggestions for one tag: other confirmed tags it could go under, and other
/// confirmed tags that could go under it (the same underlying rows, seen from each side - see
/// "Tag hierarchy" in docs/database.md).
/// </summary>
public sealed record TagParentSuggestionsResponse(
    IReadOnlyList<TagSuggestionResponse> SuggestedParents,
    IReadOnlyList<TagSuggestionResponse> SuggestedChildren);
