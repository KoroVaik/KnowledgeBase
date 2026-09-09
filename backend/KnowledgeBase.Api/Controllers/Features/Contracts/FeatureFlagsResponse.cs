namespace KnowledgeBase.Api.Controllers.Features.Contracts;

public sealed record FeatureFlagsResponse(
    bool UploadEnabled,
    bool DownloadEnabled,
    bool GoogleSignInEnabled);
