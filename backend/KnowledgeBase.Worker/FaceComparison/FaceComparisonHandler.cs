using System.Security.Cryptography;
using System.Text.Json;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceComparison;

public sealed class FaceComparisonHandler(KnowledgeBaseDbContext database, IAssetContentReader reader,
    FaceComparisonRunner runner, IChangeNotifier notifier) : IPipelineHandler
{
    public JobKind Kind => JobKind.CompareFaceDetectors;
    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var run = await database.FaceComparisonRuns.Include(item => item.Results)
            .SingleOrDefaultAsync(item => item.JobId == job.Id, cancellationToken)
            ?? throw new SkippableContentException("This comparison was removed.");
        if (run.Results.All(result => result.CompletedAtUtc != null)) return null;
        var asset = await database.Assets.SingleAsync(item => item.Id == run.AssetId, cancellationToken);
        var bytes = await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken);
        run.ContentSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        using var image = Image.Load<Rgb24>(bytes);
        image.Mutate(context => context.AutoOrient());
        run.ImageWidth = image.Width;
        run.ImageHeight = image.Height;
        foreach (var result in run.Results.Where(item => item.CompletedAtUtc == null).OrderBy(item => item.ModelId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var output = await runner.RunAsync(result.ModelId, image, cancellationToken);
                result.ConfigurationJson = output.ConfigurationJson;
                result.ElapsedMilliseconds = output.ElapsedMilliseconds;
                result.Detections = output.Detections.Select((face, index) => new FaceComparisonDetection
                {
                    Id = Guid.NewGuid().ToString("N"), ResultId = result.Id, Ordinal = index + 1,
                    X = face.Bounds.X, Y = face.Bounds.Y, Width = face.Bounds.Width, Height = face.Bounds.Height,
                    Score = face.Score, LandmarksJson = JsonSerializer.Serialize(face.Landmarks),
                    WarningsJson = JsonSerializer.Serialize(FaceComparisonQuality.Warnings(face, image.Width, image.Height)),
                    IsFace = true,
                }).ToList();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                result.Error = error.Message[..Math.Min(error.Message.Length, 2000)];
            }
            result.CompletedAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        }
        return null;
    }
}
