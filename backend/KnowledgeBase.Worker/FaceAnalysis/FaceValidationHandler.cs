using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Ai.Configuration;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class FaceValidationHandler(KnowledgeBaseDbContext database, IAssetContentReader reader,
    IContentAnalyzer analyzer, IOptions<OllamaOptions> options) : IPipelineHandler
{
    public JobKind Kind => JobKind.ValidateFaces;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var asset = await database.Assets.SingleAsync(item => item.Id == job.AssetId, cancellationToken);
        var canonicalId = await database.Assets.Where(item => item.ContentSha256 == asset.ContentSha256 && asset.ContentSha256 != null || item.Id == asset.Id)
            .OrderBy(item => item.UploadedAtUtc).ThenBy(item => item.Id).Select(item => item.Id).FirstAsync(cancellationToken);
        if (canonicalId != asset.Id) return null;
        var faces = (await (from face in database.FaceOccurrences
            join run in database.PhotoAnalysisRuns on face.RunId equals run.Id
            where face.AssetId == asset.Id && run.PipelineVersion == FaceAnalysisPipeline.CurrentDetectionVersion && face.IdentityId != null
            select face).ToListAsync(cancellationToken))
            .GroupBy(face => face.IdentityId!).Select(group => group.OrderByDescending(face => face.CreatedAtUtc).First()).ToList();
        var configurationHash = FaceValidationPrompt.ConfigurationHash(options.Value.Model);
        Image<Rgb24>? image = null;
        try
        {
            foreach (var face in faces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A user can review a face while this asset's earlier faces are being processed.
                var states = await FaceIdentityState.LoadAsync(database, cancellationToken);
                if (states.SettledIds.Contains(face.IdentityId!)
                    || await database.FaceValidationReviewDecisions.AnyAsync(item => item.FaceIdentityId == face.IdentityId, cancellationToken)) continue;
                var inputHash = FaceValidationImages.InputHash(face, asset.ContentSha256 ?? asset.Id);
                var validation = await database.FaceValidations.SingleOrDefaultAsync(item => item.FaceOccurrenceId == face.Id, cancellationToken);
                if (validation?.CompletedAtUtc is not null && validation.ConfigurationHash == configurationHash && validation.InputHash == inputHash) continue;
                validation ??= new FaceValidation { FaceOccurrenceId = face.Id, PipelineVersion = FaceValidationPolicy.PipelineVersion,
                    ModelKey = options.Value.Model, ConfigurationHash = configurationHash, InputHash = inputHash };
                if (database.Entry(validation).State == EntityState.Detached) database.FaceValidations.Add(validation);
                validation.PipelineVersion = FaceValidationPolicy.PipelineVersion;
                validation.ModelKey = options.Value.Model;
                validation.ConfigurationHash = configurationHash;
                validation.InputHash = inputHash;
                validation.Subject = null;
                validation.Evidence = null;
                validation.CompletedAtUtc = null;
                validation.LastError = null;
                try
                {
                    if (image is null)
                    {
                        image = Image.Load<Rgb24>(await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken));
                        image.Mutate(ctx => ctx.AutoOrient());
                    }
                    FaceValidationImages.Measure(image, face, validation);
                    await database.SaveChangesAsync(cancellationToken);
                    var inputs = FaceValidationImages.Prepare(image, face);
                    var answer = await analyzer.RunAsync<FaceValidationAnswer>(new AiTask(FaceValidationPrompt.System, FaceValidationPrompt.User,
                        FaceValidationPrompt.Schema(), inputs.Crop, [inputs.Context], FaceValidationPrompt.Generation), cancellationToken);
                    validation.Subject = FaceValidationPrompt.Parse(answer);
                    validation.Evidence = answer.Evidence;
                    validation.CompletedAtUtc = DateTime.UtcNow;
                    await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
                    // Checkpoint each result so a later failed request never re-runs completed faces.
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    validation.LastError = "Validation could not finish. Retry the check or review this face manually.";
                    await database.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException("Face validation did not complete.", error);
                }
            }
        }
        finally { image?.Dispose(); }
        await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        return null;
    }
}
