namespace KnowledgeBase.Api.Controllers.Notes.Contracts;

public sealed record NoteSummaryResponse(
    string Id,
    string Title,
    string Category,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
