using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.Extraction;

namespace KnowledgeBase.Worker;

// Throwaway analyzer smoke test, kept until the worker has an integration test:
//   dotnet run --project backend/KnowledgeBase.Worker -- analyze <file>
public static class AnalyzeCommand
{
    public static bool Matches(string[] args) => args is ["analyze", ..];

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        if (args is not ["analyze", var path])
        {
            Console.Error.WriteLine("Usage: dotnet run --project backend/KnowledgeBase.Worker -- analyze <path-to-file>");
            return;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No such file: {path}");
            return;
        }

        // The CLI has no MIME type from a browser; the extension carries the classification.
        var kind = ProcessableContent.Classify(string.Empty, path);

        if (kind is null)
        {
            Console.Error.WriteLine($"Unsupported file type: {path}");
            return;
        }

        using var scope = services.CreateScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IContentAnalyzer>();
        var extractor = scope.ServiceProvider.GetRequiredService<SourceExtractorSelector>().For(kind.Value);
        var bytes = await File.ReadAllBytesAsync(path);

        try
        {
            await analyzer.EnsureModelAvailableAsync(CancellationToken.None);

            var extracted = await extractor.ExtractAsync(
                new SourceAsset(bytes, string.Empty, path),
                CancellationToken.None);

            var result = await analyzer.AnalyzeAsync(
                new AnalysisRequest(ExistingTitles: [], KnownTags: [], extracted.Text, extracted.Image),
                CancellationToken.None);

            Console.WriteLine(JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
        }
    }
}
