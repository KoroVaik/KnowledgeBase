namespace KnowledgeBase.Api.Controllers.Jobs.Contracts;

/// <summary>
/// One job the worker gave up on after its last attempt. <c>AssetFileName</c> is set only for
/// jobs tied to a file. <c>Error</c> is the final attempt's failure message.
/// </summary>
public sealed record FailedJobResponse(
    string Id,
    string Kind,
    string KindDescription,
    string? AssetId,
    string? AssetFileName,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    int Attempts,
    string? Error);
