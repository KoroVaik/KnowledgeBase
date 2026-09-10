namespace KnowledgeBase.Core.Pipeline.Extraction;

// Indexes the registered extractors by kind once. A second extractor claiming the same kind
// is a wiring mistake and fails here, at startup, not on the job that happens to hit it.
public sealed class SourceExtractorSelector
{
    private readonly IReadOnlyDictionary<ContentKind, ISourceExtractor> _byKind;

    public SourceExtractorSelector(IEnumerable<ISourceExtractor> extractors)
    {
        _byKind = extractors.ToDictionary(extractor => extractor.Kind);
    }

    public ISourceExtractor For(ContentKind kind) =>
        _byKind.TryGetValue(kind, out var extractor)
            ? extractor
            : throw new InvalidOperationException($"No extractor is registered for {kind} content.");
}
