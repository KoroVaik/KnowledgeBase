namespace KnowledgeBase.Api.Controllers.Features.Contracts;

public sealed record FeatureFlagsResponse(
    bool UploadEnabled,
    bool DownloadEnabled,
    bool GoogleSignInEnabled,
    // Not a flag - the pipeline's text-size limit, so the UI can warn before an upload that
    // a large text file will likely be skipped rather than turned into a note.
    int MaxSourceChars);
