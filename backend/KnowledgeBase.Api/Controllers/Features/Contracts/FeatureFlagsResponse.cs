namespace KnowledgeBase.Api.Controllers.Features.Contracts;

public sealed record FeatureFlagsResponse(
    bool UploadEnabled,
    bool GoogleSignInEnabled,
    // Not a flag - the pipeline's text-size limit, so the UI can warn before an upload that
    // a large text file will likely be skipped rather than turned into a note.
    int MaxSourceChars,
    // The hard cap upload-link refuses. Unlike MaxSourceChars this is a wall, not a warning:
    // the UI needs it to tell "will upload but probably stays a file" from "will not upload".
    long MaxUploadBytes);
