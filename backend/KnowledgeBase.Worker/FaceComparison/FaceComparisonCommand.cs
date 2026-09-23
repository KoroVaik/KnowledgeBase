using System.Text.Json;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceComparison;

public static class FaceComparisonCommand
{
    public static async Task RunAsync(string path)
    {
        var options = Options.Create(new FaceComparisonOptions());
        var runner = new FaceComparisonRunner(new ComparisonModelFiles(options), options, Options.Create(new FaceAnalysisOptions()));
        using var image = Image.Load<Rgb24>(path);
        image.Mutate(context => context.AutoOrient());
        foreach (var model in FaceComparisonModels.All)
        {
            var result = await runner.RunAsync(model.Id, image, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(new { model = model.Name, image.Width, image.Height,
                result.ElapsedMilliseconds, configuration = JsonSerializer.Deserialize<JsonElement>(result.ConfigurationJson),
                faces = result.Detections.Select(face => new { face.Bounds, face.Score, face.Landmarks,
                    warnings = FaceComparisonQuality.Warnings(face, image.Width, image.Height) }) }));
        }
    }
}
