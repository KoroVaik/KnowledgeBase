namespace Backend.Infrastructure.Storage;

internal static class AssetFileName
{
    public static string For(string id, string? originalFileName) => id + SafeExtension(originalFileName);

    // Never trust the client-supplied name: keep only a plausible extension.
    private static string SafeExtension(string? originalFileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(originalFileName)) ?? string.Empty;

        return extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? string.Empty
            : extension;
    }
}
