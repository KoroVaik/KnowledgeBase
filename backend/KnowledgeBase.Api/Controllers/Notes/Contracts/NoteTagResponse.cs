namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

/// <summary>One tag on a note. The list is ordered by relevance; the first is the primary.</summary>
public sealed record NoteTagResponse(string Name, bool Confirmed);
