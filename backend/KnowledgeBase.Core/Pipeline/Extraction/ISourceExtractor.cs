using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.Extraction;

// The raw bytes of an uploaded file plus metadata an extractor might need.
public sealed record SourceAsset(byte[] Bytes, string ContentType, string FileName);

// What an extractor produces. Exactly one of Text / Image for now.
public sealed record ExtractedContent(string? Text = null, AnalysisImage? Image = null);

// One per ContentKind. A new file type is a new implementation in DI - nothing else changes.
public interface ISourceExtractor
{
    ContentKind Kind { get; }

    Task<ExtractedContent> ExtractAsync(SourceAsset source, CancellationToken cancellationToken);
}
