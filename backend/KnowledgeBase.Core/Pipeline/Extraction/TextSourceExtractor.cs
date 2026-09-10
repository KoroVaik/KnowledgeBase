using System.Text;
using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.Extraction;

public sealed class TextSourceExtractor : ISourceExtractor
{
    public ContentKind Kind => ContentKind.Text;

    public Task<ExtractedContent> ExtractAsync(SourceAsset source, CancellationToken cancellationToken)
    {
        // StreamReader over a MemoryStream so a UTF-8 BOM is detected and stripped rather than
        // left as a stray character at the start of the note.
        using var reader = new StreamReader(new MemoryStream(source.Bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SkippableContentException("The file has no text content.");
        }

        return Task.FromResult(new ExtractedContent(Text: text));
    }
}
