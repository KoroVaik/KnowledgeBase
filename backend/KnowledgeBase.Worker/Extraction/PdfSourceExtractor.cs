using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.Extraction;

namespace KnowledgeBase.Worker.Extraction;

// Text layer only. A scan (no text) is Skipped, not Failed; page-image OCR is a later step
// (docs/ai-pipeline.md).
public sealed class PdfSourceExtractor(IPdfTextExtractor text) : ISourceExtractor
{
    public ContentKind Kind => ContentKind.Pdf;

    public Task<ExtractedContent> ExtractAsync(SourceAsset source, CancellationToken cancellationToken)
    {
        string extracted;

        try
        {
            extracted = text.Extract(source.Bytes);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Corrupt / truncated / password-protected - no retry helps.
            throw new SkippableContentException($"The PDF could not be read: {error.Message}");
        }

        if (string.IsNullOrWhiteSpace(extracted))
        {
            throw new SkippableContentException(
                "The PDF has no extractable text - it is most likely a scan. Page-image OCR is "
                + "not supported yet.");
        }

        return Task.FromResult(new ExtractedContent(Text: extracted));
    }
}
