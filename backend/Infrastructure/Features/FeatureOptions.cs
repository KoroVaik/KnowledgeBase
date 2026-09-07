namespace Backend.Infrastructure.Features;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool UploadEnabled { get; init; } = true;

    public bool DownloadEnabled { get; init; } = true;
}
