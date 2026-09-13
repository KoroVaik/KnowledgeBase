namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

/// <summary>
/// One tag on a note. The list is ordered by relevance; the first is the primary.
/// <c>PossiblyCombined</c>: unconfirmed and shaped like several tags in one.
/// </summary>
public sealed record NoteTagResponse(string Name, bool Confirmed, bool PossiblyCombined);
