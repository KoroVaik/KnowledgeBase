using System.Text.Json;
using KnowledgeBase.Core.Ai;

namespace KnowledgeBase.Worker;

// A throwaway smoke test for the analyzer, kept until the worker has its own integration test:
//   dotnet run --project backend/KnowledgeBase.Worker -- analyze <path-to-text-file>
// Reads the file, runs it through IContentAnalyzer, prints the AnalysisResult.
public static class AnalyzeCommand
{
    public static bool Matches(string[] args) => args is ["analyze", ..];

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        if (args is not ["analyze", var path])
        {
            Console.Error.WriteLine("Usage: dotnet run --project backend/KnowledgeBase.Worker -- analyze <path-to-text-file>");
            return;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No such file: {path}");
            return;
        }

        using var scope = services.CreateScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IContentAnalyzer>();
        var text = await File.ReadAllTextAsync(path);

        try
        {
            await analyzer.EnsureModelAvailableAsync(CancellationToken.None);

            var result = await analyzer.AnalyzeAsync(
                new AnalysisRequest(ExistingTitles: [], KnownCategories: [], Text: text),
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
