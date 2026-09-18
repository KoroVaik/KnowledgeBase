using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Worker.FaceAnalysis;

public sealed class FaceRescoreHandler(KnowledgeBaseDbContext database) : IPipelineHandler
{
    public JobKind Kind => JobKind.RescoreFaces;

    public bool RequiresContentAnalyzer => false;

    public async Task<Note?> HandleAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var references = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join person in database.People on reference.PersonId equals person.Id
            select new PersonReferenceEmbedding(person.Id, person.Name, occurrence.Id, occurrence.Embedding)).ToListAsync(cancellationToken);

        // A confirmed face is a reference, and a rejected suggestion must not come back as a fresh
        // one - both stay untouched. A confirmation that was revoked leaves neither, and its face is
        // back in review with a stale copy of the original proposal, so that one is re-scored.
        var settledOccurrenceIds = (await database.PersonReferenceFaces
            .Select(reference => reference.FaceOccurrenceId).ToListAsync(cancellationToken)).ToHashSet();
        settledOccurrenceIds.UnionWith(await (
            from candidate in database.PhotoAnalysisCandidates
            join decision in database.PhotoAnalysisReviewDecisions on candidate.Id equals decision.CandidateId
            where candidate.SubjectFaceOccurrenceId != null
                && (decision.Kind == PhotoAnalysisDecisionKind.Rejected || decision.Kind == PhotoAnalysisDecisionKind.Merged)
            select candidate.SubjectFaceOccurrenceId!).ToListAsync(cancellationToken));

        var decidedCandidateIds = (await database.PhotoAnalysisReviewDecisions
            .Select(decision => decision.CandidateId).ToListAsync(cancellationToken)).ToHashSet();

        var canonicalAssetIds = (await database.Assets.ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First().Id)
            .ToHashSet();

        var sourceRuns = (await database.PhotoAnalysisRuns.ToListAsync(cancellationToken)).ToDictionary(run => run.Id);

        // An occurrence of an older detection version has no open candidates - the newer run
        // superseded them - so re-ranking it writes its stale box back into review as a fresh
        // proposal. Only the current version's detections are live ranking subjects.
        var openOccurrences = (await database.FaceOccurrences.ToListAsync(cancellationToken))
            .Where(occurrence => !settledOccurrenceIds.Contains(occurrence.Id)
                && canonicalAssetIds.Contains(occurrence.AssetId)
                && sourceRuns.TryGetValue(occurrence.RunId, out var sourceRun)
                && sourceRun.PipelineVersion == FaceAnalysisPipeline.CurrentDetectionVersion)
            .ToList();
        if (openOccurrences.Count == 0) return null;

        var currentByOccurrenceId = (await database.PhotoAnalysisCandidates
                .Where(candidate => candidate.Kind == PhotoAnalysisCandidateKind.Person && candidate.SupersededAtUtc == null)
                .ToListAsync(cancellationToken))
            .Where(candidate => candidate.SubjectFaceOccurrenceId is not null && !decidedCandidateIds.Contains(candidate.Id))
            .GroupBy(candidate => candidate.SubjectFaceOccurrenceId!)
            .ToDictionary(group => group.Key, group => group.OrderBy(candidate => candidate.Rank).ToList());

        var now = DateTime.UtcNow;

        foreach (var perAsset in openOccurrences.GroupBy(occurrence => occurrence.AssetId))
        {
            var runId = Guid.NewGuid().ToString("N");
            var written = new List<PhotoAnalysisCandidate>();
            var retired = new List<PhotoAnalysisCandidate>();

            foreach (var occurrence in perAsset)
            {
                var current = currentByOccurrenceId.GetValueOrDefault(occurrence.Id, []);
                var storedByTarget = current.GroupBy(TargetKey).ToDictionary(group => group.Key, group => group.OrderByDescending(candidate => candidate.CreatedAtUtc).First());
                var fresh = FaceCandidateRanking.For(runId, occurrence, references, now);

                foreach (var candidate in fresh)
                {
                    var stored = storedByTarget.GetValueOrDefault(TargetKey(candidate));
                    if (stored is not null && !FaceCandidateRanking.MovedEnough(stored.Score, candidate.Score)) continue;
                    if (stored is not null) retired.Add(stored);
                    written.Add(candidate);
                }

                // A person who fell under the floor, or whose reference faces are gone, has no fresh row
                // at all: their stale score would otherwise stay on screen forever.
                var freshTargets = fresh.Select(TargetKey).ToHashSet();
                retired.AddRange(current.Where(candidate => !freshTargets.Contains(TargetKey(candidate))));
            }

            if (written.Count == 0 && retired.Count == 0) continue;

            if (written.Count > 0)
            {
                var sourceRun = sourceRuns[perAsset.First().RunId];
                database.PhotoAnalysisRuns.Add(new PhotoAnalysisRun
                {
                    // The embeddings are the detection run's, so its model and configuration still
                    // describe this result - only the ranking is new.
                    Id = runId, AssetId = perAsset.Key, PipelineVersion = "face-rescore/v1",
                    ModelKey = sourceRun.ModelKey, ConfigurationHash = sourceRun.ConfigurationHash, CompletedAtUtc = now
                });
                database.PhotoAnalysisCandidates.AddRange(written);
            }

            foreach (var candidate in retired)
            {
                candidate.SupersededAtUtc = now;
            }
        }

        return null;
    }

    // The unknown-face proposal carries no target id; it is still one slot per face, so it compares
    // against the stored unknown row rather than looking like a different person every time.
    private static string TargetKey(PhotoAnalysisCandidate candidate) => candidate.ProposedTargetId ?? string.Empty;
}
