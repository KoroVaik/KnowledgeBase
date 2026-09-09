namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

public sealed record NoteResponse(
    string Id,
    string Title,
    string Category,
    string Body,
    string? SourceAssetId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
