namespace KnowledgeBase.Api.Controllers.Jobs.Contracts;

/// <summary>How many failed jobs were put back in the queue.</summary>
public sealed record RetryFailedJobsResponse(int Requeued);
