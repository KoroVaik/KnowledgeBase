using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Core.Pipeline.Extraction;

// The raw bytes of an uploaded file plus the metadata an extractor might need to make sense
// of them.
public sealed record SourceAsset(byte[] Bytes, string ContentType, string FileName);

// What an extractor produces: something the analyzer can take. Exactly one of Text / Image is
// set for now; when a kind yields both (PDF pages rendered alongside their text) this record
// grows and AnalysisRequest with it.
public sealed record ExtractedContent(string? Text = null, AnalysisImage? Image = null);

// One per ContentKind. The worker reads the bytes from storage once, then hands them to the
// extractor whose Kind matches ProcessableContent.Classify. A new file type is a new
// implementation registered in DI - nothing else in the pipeline changes.
public interface ISourceExtractor
{
    ContentKind Kind { get; }

    Task<ExtractedContent> ExtractAsync(SourceAsset source, CancellationToken cancellationToken);
}
