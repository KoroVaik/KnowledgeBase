namespace Backend.Infrastructure.Storage;

internal static class AssetFileName
{
    public static string For(string id, string? originalFileName) => id + SafeExtension(originalFileName);

    // The inverse of For: after a direct upload the API only ever sees the stored name, and
    // the row still wants the id that name was built from.
    public static string IdOf(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    // The name comes back from the route and turns into a file path or an object key, so a
    // name that could climb out of the assets folder is rejected before it gets there.
    public static bool IsSafe(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName == Path.GetFileName(fileName)
        && fileName != "."
        && fileName != ".."
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    // Never trust the client-supplied name: keep only a plausible extension.
    private static string SafeExtension(string? originalFileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(originalFileName)) ?? string.Empty;

        return extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? string.Empty
            : extension;
    }
}
