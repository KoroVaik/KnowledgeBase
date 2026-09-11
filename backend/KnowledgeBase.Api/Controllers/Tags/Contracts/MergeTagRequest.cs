namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>The tag that absorbs the one named in the route.</summary>
public sealed record MergeTagRequest(string IntoId);
