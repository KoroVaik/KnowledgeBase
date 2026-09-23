using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Worker.FaceAnalysis;

// Regroups every open face of the archive into review rows and rewrites the whole open review
// set: each run supersedes every undecided person candidate and writes one fresh candidate per
// open face, pointing at the row (FaceCluster) it landed in.
public sealed class ClusterFacesHandler(KnowledgeBaseDbContext database, IOptions<FaceClusteringOptions> options) : IPipelineHandler
{
    public const string PipelineVersion = "face-clustering/v1";

    public JobKind Kind => JobKind.ClusterFaces;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        // A batch of uploads would otherwise regroup the whole archive once per photo. The last
        // detection job queues another ClusterFaces, so skipping here loses nothing.
        if (await database.ProcessingJobs.AnyAsync(
                other => other.Id != job.Id
                    && (other.Kind == JobKind.FingerprintAsset || other.Kind == JobKind.AnalyzeFaces)
                    && (other.Status == ProcessingStatus.Pending || other.Status == ProcessingStatus.Running),
                cancellationToken))
            return null;

        var thresholds = options.Value;
        var now = DateTime.UtcNow;
        var states = await FaceIdentityState.LoadAsync(database, cancellationToken);
        var people = await database.People.ToDictionaryAsync(person => person.Id, person => person.Name, cancellationToken);
        var references = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join person in database.People on reference.PersonId equals person.Id
            where !occurrence.IsPartial
            select new PersonReferenceEmbedding(person.Id, person.Name, occurrence.Id, occurrence.Embedding)).ToListAsync(cancellationToken);

        var canonicalAssetIds = (await database.Assets.ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First().Id)
            .ToHashSet();
        var currentRuns = await database.PhotoAnalysisRuns
            .Where(run => run.PipelineVersion == FaceAnalysisPipeline.CurrentDetectionVersion)
            .ToDictionaryAsync(run => run.Id, cancellationToken);
        var currentRunIds = currentRuns.Keys.ToList();

        // Only the current detection version's faces are review subjects: an older version's box
        // points at the wrong part of the picture. One identity has one occurrence per version.
        var openOccurrences = (await database.FaceOccurrences
                .Where(occurrence => occurrence.IdentityId != null && currentRunIds.Contains(occurrence.RunId))
                .ToListAsync(cancellationToken))
            .Where(occurrence => canonicalAssetIds.Contains(occurrence.AssetId) && !states.SettledIds.Contains(occurrence.IdentityId!))
            .GroupBy(occurrence => occurrence.IdentityId!)
            .Select(group => group.OrderByDescending(occurrence => occurrence.CreatedAtUtc).First())
            .ToList();

        // Vectors of two embedders cannot be compared; a model swap leaves them mixed only until
        // the migration job has re-embedded everything, and that job queues a regrouping itself.
        var embeddingLength = openOccurrences.Select(occurrence => occurrence.Embedding.Length)
            .Concat(references.Select(reference => reference.Embedding.Length))
            .GroupBy(length => length)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        openOccurrences = openOccurrences.Where(occurrence => occurrence.Embedding.Length == embeddingLength).ToList();
        references = references.Where(reference => reference.Embedding.Length == embeddingLength).ToList();
        // A crop at a photo edge is a real face, but its embedding should not automatically join a
        // person or train future suggestions. It is still emitted as an explicitly marked Unsorted row.
        var partialOccurrences = openOccurrences.Where(occurrence => occurrence.IsPartial).ToList();
        openOccurrences = openOccurrences.Where(occurrence => !occurrence.IsPartial).ToList();

        var result = FaceClustering.Run(
            openOccurrences.Select(occurrence => new FaceClusteringFace(occurrence.IdentityId!, occurrence.Embedding)).ToList(),
            references,
            states,
            thresholds);

        var configurationHash = ConfigurationHash(thresholds);
        var clusteringRun = new FaceClusteringRun
        {
            Id = Guid.NewGuid().ToString("N"), PipelineVersion = PipelineVersion,
            ConfigurationHash = configurationHash, CompletedAtUtc = now
        };
        database.FaceClusteringRuns.Add(clusteringRun);

        // Tracked rather than a bulk update, so superseding the old set and writing the new one
        // commit together: a failed run must not leave the review screen empty.
        var stale = await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.Kind == PhotoAnalysisCandidateKind.Person && candidate.SupersededAtUtc == null
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ToListAsync(cancellationToken);
        foreach (var candidate in stale) candidate.SupersededAtUtc = now;

        var placements = new List<Placement>();
        FaceCluster AddCluster(FaceClusterKind kind, string? personId = null, string? ignoredGroupId = null, string? hintPersonId = null, double? hintScore = null)
        {
            var cluster = new FaceCluster
            {
                Id = Guid.NewGuid().ToString("N"), RunId = clusteringRun.Id, Kind = kind, PersonId = personId,
                IgnoredGroupId = ignoredGroupId, HintPersonId = hintPersonId, HintScore = hintScore, CreatedAtUtc = now
            };
            database.FaceClusters.Add(cluster);
            return cluster;
        }

        foreach (var personGroup in result.PersonJoins.GroupBy(join => join.PersonId))
        {
            var cluster = AddCluster(FaceClusterKind.Person, personId: personGroup.Key);
            placements.AddRange(personGroup.Select(join => new Placement(join.IdentityId, cluster, personGroup.Count(), join.PersonId, join.BestReferenceFaceOccurrenceId)));
        }
        foreach (var group in result.AnonymousClusters)
        {
            var cluster = AddCluster(FaceClusterKind.Anonymous, hintPersonId: group.HintPersonId, hintScore: group.HintScore);
            placements.AddRange(group.IdentityIds.Select(identityId => new Placement(identityId, cluster, group.IdentityIds.Count, null, null)));
        }
        foreach (var group in result.IgnoredGroups)
        {
            var cluster = AddCluster(FaceClusterKind.Ignored, ignoredGroupId: group.GroupId, hintPersonId: group.HintPersonId, hintScore: group.HintScore);
            placements.AddRange(group.IdentityIds.Select(identityId => new Placement(identityId, cluster, group.IdentityIds.Count, null, null)));
        }
        if (result.UnsortedIdentityIds.Count > 0)
        {
            var cluster = AddCluster(FaceClusterKind.Unsorted);
            placements.AddRange(result.UnsortedIdentityIds.Select(identityId => new Placement(identityId, cluster, 1, null, null)));
        }
        if (partialOccurrences.Count > 0)
        {
            var cluster = AddCluster(FaceClusterKind.Unsorted);
            placements.AddRange(partialOccurrences.Select(occurrence => new Placement(occurrence.IdentityId!, cluster, 1, null, null)));
        }

        var occurrenceByIdentity = openOccurrences.Concat(partialOccurrences).ToDictionary(occurrence => occurrence.IdentityId!);
        var runIdByAsset = new Dictionary<string, string>();
        foreach (var rowGroup in placements.GroupBy(placement => placement.Cluster.Id))
        {
            var rank = 0;
            foreach (var placement in rowGroup.OrderByDescending(placement => result.OrderScores.GetValueOrDefault(placement.IdentityId)))
            {
                rank++;
                var occurrence = occurrenceByIdentity[placement.IdentityId];
                if (!runIdByAsset.TryGetValue(occurrence.AssetId, out var runId))
                {
                    runId = Guid.NewGuid().ToString("N");
                    runIdByAsset[occurrence.AssetId] = runId;
                    database.PhotoAnalysisRuns.Add(new PhotoAnalysisRun
                    {
                        // The embeddings are the detection run's, so its model still describes this
                        // result; the configuration is the clustering's own.
                        Id = runId, AssetId = occurrence.AssetId, PipelineVersion = PipelineVersion,
                        ModelKey = currentRuns[occurrence.RunId].ModelKey, ConfigurationHash = configurationHash, CompletedAtUtc = now
                    });
                }

                var cluster = placement.Cluster;
                database.PhotoAnalysisCandidates.Add(new PhotoAnalysisCandidate
                {
                    Id = Guid.NewGuid().ToString("N"), RunId = runId, Kind = PhotoAnalysisCandidateKind.Person,
                    SubjectAssetId = occurrence.AssetId, SubjectFaceOccurrenceId = occurrence.Id,
                    ProposedTargetId = placement.PersonId,
                    ProposedLabel = placement.PersonId is not null ? people.GetValueOrDefault(placement.PersonId, "Unknown person") : $"{cluster.Kind} face",
                    Rank = rank, Score = result.OrderScores.GetValueOrDefault(placement.IdentityId),
                    SignalsJson = JsonSerializer.Serialize(new
                    {
                        metric = "cosine",
                        embedder = occurrence.EmbeddingModelKey,
                        clustering = PipelineVersion,
                        thresholds = new { personJoin = thresholds.PersonJoinThreshold, cluster = thresholds.ClusterThreshold, hint = thresholds.HintThreshold },
                        clusterKind = cluster.Kind.ToString(),
                        clusterSize = placement.ClusterSize,
                        hint = cluster.HintPersonId is null ? null : new { personId = cluster.HintPersonId, score = cluster.HintScore },
                        referenceFaceId = placement.ReferenceFaceOccurrenceId,
                        detectionScore = occurrence.DetectionScore,
                    }),
                    CreatedAtUtc = now, FaceClusterId = cluster.Id,
                });
            }
        }

        Console.WriteLine($"[face-clustering] {openOccurrences.Count} open face(s): {result.PersonJoins.Count} joined people, {result.AnonymousClusters.Count} anonymous group(s), {result.IgnoredGroups.Count} ignored group(s), {result.UnsortedIdentityIds.Count} unsorted.");
        return null;
    }

    private static string ConfigurationHash(FaceClusteringOptions thresholds) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"{PipelineVersion}|{thresholds.PersonJoinThreshold}|{thresholds.ClusterThreshold}|{thresholds.HintThreshold}"))));

    private sealed record Placement(string IdentityId, FaceCluster Cluster, int ClusterSize, string? PersonId, string? ReferenceFaceOccurrenceId);
}
