namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

public sealed record NoteResponse(
    string Id,
    string Title,
    string Category,
    string Kind,
    string Body,
    IReadOnlyList<NoteLinkStateResponse> Links,
    string? SourceAssetId,
    string? SourceFileName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DeletedAtUtc);
