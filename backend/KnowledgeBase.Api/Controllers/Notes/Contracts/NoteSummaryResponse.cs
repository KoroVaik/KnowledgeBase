namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

public sealed record NoteSummaryResponse(
    string Id,
    string Title,
    IReadOnlyList<NoteTagResponse> Tags,
    string Kind,
    string? SourceAssetId,
    string? SourceFileName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DeletedAtUtc);
