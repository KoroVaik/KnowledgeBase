namespace Backend.Infrastructure.Features;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool UploadEnabled { get; init; } = true;

    public bool DownloadEnabled { get; init; } = true;

    public bool GoogleSignInEnabled { get; init; }

    // Off by default so the code can ship before the bucket has CORS rules: with it off the
    // bytes keep flowing through the API exactly as before.
    public bool DirectAssetAccessEnabled { get; init; }
}
