namespace KnowledgeBase.Api.Controllers.Assets.Contracts;

public sealed record UploadLinkRequest(string? FileName, string? ContentType, long SizeBytes);
