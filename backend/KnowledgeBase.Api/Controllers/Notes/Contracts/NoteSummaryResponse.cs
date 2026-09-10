namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

public sealed record NoteSummaryResponse(
    string Id,
    string Title,
    string Category,
    string? SourceAssetId,
    string? SourceFileName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
