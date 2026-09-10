namespace KnowledgeBase.Core.Storage;

public static class AssetFileName
{
    public static string For(string id, string? originalFileName) => id + SafeExtension(originalFileName);

    // Inverse of For: the API sees only the stored name but the row wants the id it was built from.
    public static string IdOf(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    // The name comes from the route and becomes a path / object key - reject anything that
    // could climb out of the folder.
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
