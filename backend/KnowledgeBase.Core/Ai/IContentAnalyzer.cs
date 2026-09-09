namespace KnowledgeBase.Core.Ai;

// Exactly one of Text / Image is set - the worker fills whichever the file is.
public sealed record AnalysisRequest(
    IReadOnlyList<string> ExistingTitles,
    IReadOnlyList<string> KnownCategories,
    string? Text = null,
    AnalysisImage? Image = null);

public sealed record AnalysisImage(byte[] Bytes, string ContentType);

public sealed record AnalysisResult(
    string Title,
    string Category,
    string MarkdownBody,
    IReadOnlyList<string> Links);

public interface IContentAnalyzer
{
    // Called before the first analysis (by the worker, and by the analyze CLI command): a
    // pulled-model check that fails with "ollama pull ..." rather than a cryptic 404 mid-job.
    Task EnsureModelAvailableAsync(CancellationToken cancellationToken);

    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken);
}
