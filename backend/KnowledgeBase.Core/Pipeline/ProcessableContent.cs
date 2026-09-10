namespace KnowledgeBase.Core.Pipeline;

public enum ContentKind
{
    Text,
    Image,
    Pdf,
}

// The single type -> kind map. The API uses it to decide whether an upload gets a
// ProcessingJob; the worker uses the kind to pick an ISourceExtractor. Adding a file type is
// one row here plus one extractor - no switch to hunt down.
public static class ProcessableContent
{
    private static readonly Dictionary<string, ContentKind> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = ContentKind.Text,
        [".md"] = ContentKind.Text,
        [".markdown"] = ContentKind.Text,
        [".jpg"] = ContentKind.Image,
        [".jpeg"] = ContentKind.Image,
        [".png"] = ContentKind.Image,
        [".webp"] = ContentKind.Image,
        [".gif"] = ContentKind.Image,
        [".pdf"] = ContentKind.Pdf,
    };

    // Matched before the extension: a MIME type is more reliable when the browser sends one.
    private static readonly (string Prefix, ContentKind Kind)[] ByContentTypePrefix =
    [
        ("text/", ContentKind.Text),
        ("image/", ContentKind.Image),
    ];

    private static readonly Dictionary<string, ContentKind> ByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ContentKind.Pdf,
    };

    public static ContentKind? Classify(string contentType, string fileName)
    {
        if (ByContentType.TryGetValue(contentType, out var exact))
        {
            return exact;
        }

        foreach (var (prefix, kind) in ByContentTypePrefix)
        {
            if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return ByExtension.TryGetValue(Path.GetExtension(fileName), out var byExtension)
            ? byExtension
            : null;
    }
}
