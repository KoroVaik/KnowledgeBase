namespace KnowledgeBase.Api.Controllers.Tags.Contracts;

/// <summary>One tag in the vocabulary, with how many live Source notes carry it.</summary>
public sealed record TagResponse(string Name, bool Confirmed, int NoteCount);
