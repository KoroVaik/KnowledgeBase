using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.Extraction;

namespace KnowledgeBase.Worker.Extraction;

// Phase 1: the text layer only. A PDF without one (a scan) is Skipped, not Failed - rendering
// its pages to images for the vision model is a separate step (see CHECKLIST).
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
            // Corrupt, truncated or password-protected: PdfPig throws its own exception types
            // and none of them get better on a retry.
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
