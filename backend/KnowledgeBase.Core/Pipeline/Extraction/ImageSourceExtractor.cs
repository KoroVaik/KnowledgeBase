using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.Extraction;

public sealed class ImageSourceExtractor : ISourceExtractor
{
    public ContentKind Kind => ContentKind.Image;

    public Task<ExtractedContent> ExtractAsync(SourceAsset source, CancellationToken cancellationToken)
    {
        if (source.Bytes.Length == 0)
        {
            throw new SkippableContentException("The image has no content.");
        }

        return Task.FromResult(new ExtractedContent(Image: new AnalysisImage(source.Bytes, source.ContentType)));
    }
}
