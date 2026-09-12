namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>
/// One pending AI placement guess for a tag, from the child's side: the candidate parent and how
/// confident the model was. Lets the review UI offer "confirm as a child of X" inline, without a
/// separate fetch per row - see <see cref="TagResponse.PendingParentSuggestions"/>.
/// </summary>
public sealed record TagParentSuggestionRef(string ParentId, string Confidence);
