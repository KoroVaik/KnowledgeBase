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

namespace KnowledgeBase.Worker.FaceRecognitionComparison;

public sealed class FaceRecognitionComparisonHandler(
    KnowledgeBaseDbContext database,
    IAssetContentReader reader,
    FaceRecognitionComparisonRunner runner,
    IChangeNotifier notifier) : IPipelineHandler
{
    private const int EvidencePairLimit = 24;

    public JobKind Kind => JobKind.CompareFaceRecognizers;
    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var run = await database.FaceRecognitionComparisonRuns.Include(item => item.Results)
            .SingleOrDefaultAsync(item => item.JobId == job.Id, cancellationToken)
            ?? throw new SkippableContentException("This recognition comparison was removed.");
        if (run.Results.All(result => result.CompletedAtUtc is not null)) return null;

        var references = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join asset in database.Assets on occurrence.AssetId equals asset.Id
            where !occurrence.IsPartial
            select new ReferenceFace(reference.PersonId, occurrence, asset.StoredFileName)).ToListAsync(cancellationToken);
        var latestOccurrenceTimes = database.FaceOccurrences
            .GroupBy(occurrence => occurrence.AssetId)
            .Select(group => new { AssetId = group.Key, CreatedAtUtc = group.Max(occurrence => occurrence.CreatedAtUtc) });
        var newestFaces = await (
            from occurrence in database.FaceOccurrences
            join latest in latestOccurrenceTimes on new { occurrence.AssetId, occurrence.CreatedAtUtc }
                equals new { latest.AssetId, latest.CreatedAtUtc }
            join asset in database.Assets on occurrence.AssetId equals asset.Id
            select new WorkFace(occurrence, asset.StoredFileName)).ToListAsync(cancellationToken);
        var faces = newestFaces.Concat(references.Select(reference => new WorkFace(reference.Occurrence, reference.StoredFileName)))
            .GroupBy(face => face.Occurrence.Id).Select(group => group.First()).ToList();
        var modelIds = FaceRecognitionComparisonModels.All.Select(model => model.Id).ToList();
        var existing = await database.FaceRecognitionComparisonEmbeddings
            .Where(embedding => faces.Select(face => face.Occurrence.Id).Contains(embedding.FaceOccurrenceId)
                && modelIds.Contains(embedding.ModelId))
            .ToListAsync(cancellationToken);
        var embeddingByKey = existing.ToDictionary(embedding => EmbeddingKey(embedding.FaceOccurrenceId, embedding.ModelId), embedding => embedding.Embedding);
        var pending = faces.Where(face => modelIds.Any(modelId => !embeddingByKey.ContainsKey(EmbeddingKey(face.Occurrence.Id, modelId)))).ToList();
        if (pending.Count == 0) throw new SkippableContentException("All detected faces already have recognition-comparison results.");
        run.PhotoCount = pending.Select(face => face.Occurrence.AssetId).Distinct().Count();

        var inputs = pending.Select(ToInput).Where(input => input is not null).Select(input => input!).ToList();
        if (inputs.Count == 0) throw new SkippableContentException("The pending face occurrences have no usable five-point landmarks.");
        var images = new Dictionary<string, Image<Rgb24>>();
        try
        {
            foreach (var group in inputs.GroupBy(input => input.AssetId))
            {
                var storedFileName = pending.First(face => face.Occurrence.AssetId == group.Key).StoredFileName;
                var image = Image.Load<Rgb24>(await reader.ReadBytesAsync(storedFileName, cancellationToken));
                image.Mutate(context => context.AutoOrient());
                images.Add(group.Key, image);
            }
            inputs = inputs.Select(input => input with { Image = images[input.AssetId] }).ToList();

            foreach (var result in run.Results.OrderBy(result => result.ModelId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modelInputs = inputs.Where(input => !embeddingByKey.ContainsKey(EmbeddingKey(input.OccurrenceId, result.ModelId))).ToList();
                if (modelInputs.Count == 0)
                {
                    result.ConfigurationJson = JsonSerializer.Serialize(new { adapter = "face-recognition-comparison/v1", cached = true });
                    result.ElapsedMilliseconds = 0;
                    continue;
                }
                try
                {
                    var output = await runner.RunAsync(result.ModelId, modelInputs, cancellationToken);
                    result.ConfigurationJson = output.ConfigurationJson;
                    result.ElapsedMilliseconds = output.ElapsedMilliseconds;
                    foreach (var embedding in output.Embeddings)
                    {
                        var stored = new FaceRecognitionComparisonEmbedding
                        {
                            Id = Guid.NewGuid().ToString("N"), RunId = run.Id, FaceOccurrenceId = embedding.Key,
                            ModelId = result.ModelId, Embedding = embedding.Value, CreatedAtUtc = DateTime.UtcNow,
                        };
                        database.FaceRecognitionComparisonEmbeddings.Add(stored);
                        embeddingByKey.Add(EmbeddingKey(embedding.Key, result.ModelId), embedding.Value);
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    result.Error = error.Message[..Math.Min(error.Message.Length, 2000)];
                }
            }

            var successfulModels = run.Results.Where(result => result.Error is null).Select(result => result.ModelId).ToList();
            var sharedReferences = successfulModels.Count == 0 ? new List<ReferenceFace>() : references.Where(reference => successfulModels.All(modelId =>
                embeddingByKey.ContainsKey(EmbeddingKey(reference.Occurrence.Id, modelId)))).ToList();
            run.ReferenceFaceCount = sharedReferences.Count;
            run.PersonCount = sharedReferences.Select(reference => reference.PersonId).Distinct().Count();
            if (sharedReferences.Select(reference => reference.PersonId).Distinct().Count() >= 2
                && sharedReferences.GroupBy(reference => reference.PersonId).Any(group => group.Count() >= 2))
            {
                var pairsByModel = successfulModels.ToDictionary(modelId => modelId,
                    modelId => CreatePairs(sharedReferences, modelId, embeddingByKey));
                var metrics = new Dictionary<string, RecognitionComparisonMetrics>();
                foreach (var result in run.Results.Where(result => pairsByModel.ContainsKey(result.ModelId)))
                {
                    var modelMetrics = KnowledgeBase.Core.FaceAnalysis.FaceRecognitionComparison.Calibrate(pairsByModel[result.ModelId]);
                    metrics.Add(result.ModelId, modelMetrics);
                    result.Threshold = modelMetrics.Threshold;
                    result.SamePersonPairs = modelMetrics.SamePersonPairs;
                    result.DifferentPersonPairs = modelMetrics.DifferentPersonPairs;
                    result.TruePositives = modelMetrics.TruePositives;
                    result.FalsePositives = modelMetrics.FalsePositives;
                    result.TrueNegatives = modelMetrics.TrueNegatives;
                    result.FalseNegatives = modelMetrics.FalseNegatives;
                }
                StoreEvidence(run, pairsByModel, metrics);
            }
            foreach (var result in run.Results) result.CompletedAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            notifier.Publish(new(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
            return null;
        }
        finally
        {
            foreach (var image in images.Values) image.Dispose();
        }
    }

    private void StoreEvidence(FaceRecognitionComparisonRun run,
        IReadOnlyDictionary<string, List<RecognitionComparisonPair>> pairsByModel,
        IReadOnlyDictionary<string, RecognitionComparisonMetrics> metrics)
    {
        var evidence = SelectEvidence(pairsByModel, metrics).ToList();
        var resultByModel = run.Results.Where(result => pairsByModel.ContainsKey(result.ModelId)).ToDictionary(result => result.ModelId);
        var scoresByModel = pairsByModel.ToDictionary(
            item => item.Key,
            item => item.Value.ToDictionary(pair => PairKey(pair), pair => pair.Score));
        foreach (var pair in evidence)
        {
            var stored = new FaceRecognitionComparisonPair
            {
                Id = Guid.NewGuid().ToString("N"), RunId = run.Id,
                FirstFaceOccurrenceId = pair.FirstFaceOccurrenceId, SecondFaceOccurrenceId = pair.SecondFaceOccurrenceId,
                IsSamePerson = pair.IsSamePerson,
            };
            foreach (var model in pairsByModel)
            {
                var score = scoresByModel[model.Key][PairKey(pair)];
                stored.Scores.Add(new FaceRecognitionComparisonScore
                {
                    Id = Guid.NewGuid().ToString("N"), PairId = stored.Id, ResultId = resultByModel[model.Key].Id,
                    Score = score, IsMatch = KnowledgeBase.Core.FaceAnalysis.FaceRecognitionComparison.IsMatch(score, metrics[model.Key]),
                });
            }
            run.Pairs.Add(stored);
        }
    }

    private static RecognitionComparisonFaceInput? ToInput(WorkFace face)
    {
        try
        {
            var landmarks = JsonSerializer.Deserialize<FaceLandmark[]>(face.Occurrence.LandmarksJson) ?? [];
            return landmarks.Length != 5 || face.Occurrence.Width <= 0 || face.Occurrence.Height <= 0 ? null : new RecognitionComparisonFaceInput(
                face.Occurrence.Id, face.Occurrence.AssetId, face.Occurrence.X, face.Occurrence.Y,
                face.Occurrence.Width, face.Occurrence.Height, landmarks, null!);
        }
        catch (JsonException) { return null; }
    }

    private static List<RecognitionComparisonPair> CreatePairs(IReadOnlyList<ReferenceFace> faces, string modelId,
        IReadOnlyDictionary<string, float[]> embeddings)
    {
        var pairs = new List<RecognitionComparisonPair>();
        for (var first = 0; first < faces.Count; first++)
        for (var second = first + 1; second < faces.Count; second++)
        {
            var left = faces[first];
            var right = faces[second];
            pairs.Add(new RecognitionComparisonPair(left.Occurrence.Id, right.Occurrence.Id,
                left.PersonId == right.PersonId,
                FaceEmbeddingMath.CosineSimilarity(embeddings[EmbeddingKey(left.Occurrence.Id, modelId)], embeddings[EmbeddingKey(right.Occurrence.Id, modelId)])));
        }
        return pairs;
    }

    private static IEnumerable<RecognitionComparisonPair> SelectEvidence(
        IReadOnlyDictionary<string, List<RecognitionComparisonPair>> pairsByModel,
        IReadOnlyDictionary<string, RecognitionComparisonMetrics> metrics)
    {
        var models = pairsByModel.Keys.Order().ToList();
        if (models.Count == 0) return [];
        var scoresByModel = pairsByModel.ToDictionary(
            item => item.Key,
            item => item.Value.ToDictionary(pair => PairKey(pair), pair => pair));
        return pairsByModel[models[0]]
            .Select(pair =>
            {
                var scores = models.Select(model => scoresByModel[model][PairKey(pair)]).ToList();
                var matches = scores.Select((score, index) => KnowledgeBase.Core.FaceAnalysis.FaceRecognitionComparison.IsMatch(score.Score, metrics[models[index]])).ToList();
                var mistakes = matches.Count(match => match != pair.IsSamePerson);
                var disagreement = matches.Distinct().Count() > 1;
                var closest = scores.Select((score, index) => Math.Abs(score.Score - metrics[models[index]].Threshold)).Average();
                return new { Pair = pair, mistakes, disagreement, closest };
            })
            .OrderByDescending(item => item.disagreement)
            .ThenByDescending(item => item.mistakes)
            .ThenBy(item => item.closest)
            .ThenBy(item => item.Pair.FirstFaceOccurrenceId).ThenBy(item => item.Pair.SecondFaceOccurrenceId)
            .Take(EvidencePairLimit)
            .Select(item => item.Pair);
    }

    private static string EmbeddingKey(string occurrenceId, string modelId) => $"{occurrenceId}:{modelId}";
    private static string PairKey(RecognitionComparisonPair pair) => $"{pair.FirstFaceOccurrenceId}:{pair.SecondFaceOccurrenceId}";

    private sealed record WorkFace(FaceOccurrence Occurrence, string StoredFileName);
    private sealed record ReferenceFace(string PersonId, FaceOccurrence Occurrence, string StoredFileName);
}
