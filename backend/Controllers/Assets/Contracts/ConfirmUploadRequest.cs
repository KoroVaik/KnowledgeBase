namespace Backend.Controllers.Assets.Contracts;

public sealed record ConfirmUploadRequest(string? OriginalFileName, string? ContentType);
