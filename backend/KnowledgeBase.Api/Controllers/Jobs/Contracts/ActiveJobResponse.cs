namespace KnowledgeBase.Api.Controllers.Jobs.Contracts;

/// <summary>
/// One job still <c>Pending</c> or <c>Running</c>. <c>AssetFileName</c> is set only for a
/// <c>BuildSourceNote</c> job (the other kinds carry no <c>AssetId</c>). <c>Error</c> is the
/// previous attempt's failure message when the worker has retried this job before and given it
/// back to the queue - null on a job that has not failed yet.
/// </summary>
public sealed record ActiveJobResponse(
    string Id,
    string Kind,
    string Status,
    string? AssetId,
    string? AssetFileName,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    int Attempts,
    string? Error);
