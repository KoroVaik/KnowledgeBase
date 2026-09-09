namespace KnowledgeBase.Core.Pipeline;

public enum ContentKind
{
    Text,
    Image,
}

// Decides whether an uploaded file is something the AI pipeline can handle, and how to read
// it. Text and images for now; PDF and video come later.
public static class ProcessableContent
{
    private static readonly string[] TextExtensions = [".txt", ".md", ".markdown"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    public static ContentKind? Classify(string contentType, string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return ContentKind.Text;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return ContentKind.Image;
        }

        return null;
    }
}
