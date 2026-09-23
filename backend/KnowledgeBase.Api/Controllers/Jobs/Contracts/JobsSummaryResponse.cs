namespace KnowledgeBase.Api.Controllers.Jobs.Contracts;

public sealed record JobsSummaryResponse(IReadOnlyList<ActiveJobResponse> Jobs, IReadOnlyList<FailedJobResponse> Failed);
