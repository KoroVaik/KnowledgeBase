namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

/// <summary>The existing tag to attach to the note named in the route.</summary>
public sealed record AddNoteTagRequest(string TagId);
