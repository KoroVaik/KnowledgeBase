using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Ai.Configuration;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace KnowledgeBase.Worker.SceneObservations;

public sealed class SceneObservationHandler(
    KnowledgeBaseDbContext database,
    IContentAnalyzer analyzer,
    IAssetContentReader reader,
    IOptions<SceneObservationOptions> options,
    IOptions<OllamaOptions> ollamaOptions) : IPipelineHandler
{
    private readonly int _maximumObservations = Math.Clamp(options.Value.MaximumObservations, 1, 10);
    private readonly OllamaOptions _ollamaOptions = ollamaOptions.Value;

    public JobKind Kind => JobKind.AnalyzeSceneObservations;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var assetId = job.AssetId ?? throw new InvalidOperationException($"Job {job.Id} is an observation job with no asset.");
        var asset = await database.Assets.SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {assetId} no longer exists.");
        if (ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is not ContentKind.Image)
            throw new SkippableContentException("Scene observations only apply to image assets.");

        var people = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join person in database.People on reference.PersonId equals person.Id
            where occurrence.AssetId == asset.Id
            select person).Distinct().ToListAsync(cancellationToken);
        var locations = await (
            from observation in database.LocationObservations
            join location in database.Locations on observation.LocationId equals location.Id
            where observation.AssetId == asset.Id
            select location).Distinct().ToListAsync(cancellationToken);
        if (people.Count == 0 && locations.Count == 0)
            throw new SkippableContentException("Scene observations need reviewed person or location context for this photo.");

        var bytes = await reader.ReadBytesAsync(asset.StoredFileName, cancellationToken);
        var draft = await analyzer.RunAsync<SceneObservationDraft>(
            SceneObservationPrompt.TaskFor(new AnalysisImage(bytes, asset.ContentType), people, locations), cancellationToken);
        var now = DateTime.UtcNow;
        var run = new PhotoAnalysisRun
        {
            Id = Guid.NewGuid().ToString("N"), AssetId = asset.Id, PipelineVersion = "scene-observation/v1",
            ModelKey = _ollamaOptions.Model, ConfigurationHash = ConfigurationHash(), CompletedAtUtc = now
        };
        database.PhotoAnalysisRuns.Add(run);
        await database.SceneObservations
            .Where(observation => observation.AssetId == asset.Id && observation.SupersededAtUtc == null
                && !database.SceneObservationReviewDecisions.Any(decision => decision.ObservationId == observation.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(observation => observation.SupersededAtUtc, now), cancellationToken);

        foreach (var item in draft.Observations.Take(_maximumObservations))
        {
            if (!Enum.TryParse<SceneObservationKind>(item.Kind, true, out var kind)) continue;
            var description = item.Description.Trim();
            var evidence = item.Evidence.Trim();
            if (description.Length == 0 || evidence.Length == 0) continue;
            var subject = FindPerson(people, item.SubjectPersonName);
            var related = FindPerson(people, item.RelatedPersonName);
            database.SceneObservations.Add(new SceneObservation
            {
                Id = Guid.NewGuid().ToString("N"), RunId = run.Id, AssetId = asset.Id, Kind = kind,
                SubjectPersonId = subject?.Id, SubjectPersonName = subject?.Name,
                RelatedPersonId = related?.Id, RelatedPersonName = related?.Name,
                Description = description[..Math.Min(description.Length, 500)], Evidence = evidence[..Math.Min(evidence.Length, 500)],
                Confidence = Math.Clamp(item.Confidence, 0, 1), CreatedAtUtc = now
            });
        }

        return null;
    }

    private static Person? FindPerson(IEnumerable<Person> people, string name) =>
        string.IsNullOrWhiteSpace(name) ? null : people.SingleOrDefault(person => string.Equals(person.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private string ConfigurationHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{_ollamaOptions.Model}|{_ollamaOptions.Options.Temperature}|{_ollamaOptions.Options.NumCtx}|{_maximumObservations}|structured-observations/v1")));
}
