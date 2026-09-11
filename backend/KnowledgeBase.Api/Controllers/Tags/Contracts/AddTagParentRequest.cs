namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>The tag to add as a parent of the one named in the route.</summary>
public sealed record AddTagParentRequest(string ParentId);
