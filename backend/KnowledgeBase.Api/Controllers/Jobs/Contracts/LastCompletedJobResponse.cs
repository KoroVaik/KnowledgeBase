namespace KnowledgeBase.Api.Controllers.Jobs.Contracts;

/// <summary>
/// When a job of one of the requested kinds last finished as <c>Done</c> or <c>Skipped</c> -
/// null when none has yet.
/// </summary>
public sealed record LastCompletedJobResponse(DateTime? CompletedAtUtc);
