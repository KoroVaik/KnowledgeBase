namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

/// <summary>
/// A note that points here. Shown before deleting, so the confirmation can say what breaks.
/// </summary>
public sealed record BacklinkResponse(string Id, string Title, string Kind);
