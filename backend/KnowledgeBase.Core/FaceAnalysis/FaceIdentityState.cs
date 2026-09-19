using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.FaceAnalysis;

// Which faces a human has already disposed of, resolved through the identity rather than any one
// occurrence row: a re-detection stores a new occurrence for the same physical face, and its
// review state must follow the face. A reference face settles the identity. A Delete (a Rejected
// decision) no longer closes it — it pins the face to Unsorted, out of automatic clustering, and
// remembers the rejected person so that person is never auto-joined or hinted again. An Ignored
// decision files the face into its ignored group; ignored faces are not settled, so the group can
// be re-reviewed. The latest decision per identity wins; negative persons accumulate.
public static class FaceIdentityState
{
    public static async Task<FaceIdentityReviewStates> LoadAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var settled = (await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            where occurrence.IdentityId != null
            select occurrence.IdentityId!).ToListAsync(cancellationToken)).ToHashSet();

        var rows = await (
            from decision in database.PhotoAnalysisReviewDecisions
            join candidate in database.PhotoAnalysisCandidates on decision.CandidateId equals candidate.Id
            join occurrence in database.FaceOccurrences on candidate.SubjectFaceOccurrenceId equals occurrence.Id
            where candidate.Kind == PhotoAnalysisCandidateKind.Person && occurrence.IdentityId != null
            orderby decision.DecidedAtUtc, decision.Id
            select new { IdentityId = occurrence.IdentityId!, decision.Kind, decision.ChosenTargetId, candidate.ProposedTargetId }).ToListAsync(cancellationToken);

        var latest = new Dictionary<string, (PhotoAnalysisDecisionKind Kind, string? ChosenTargetId)>();
        var negatives = new Dictionary<string, HashSet<string>>();
        foreach (var row in rows)
        {
            latest[row.IdentityId] = (row.Kind, row.ChosenTargetId);
            if (row.Kind == PhotoAnalysisDecisionKind.Rejected && row.ProposedTargetId is not null)
            {
                if (!negatives.TryGetValue(row.IdentityId, out var rejected))
                    negatives[row.IdentityId] = rejected = [];
                rejected.Add(row.ProposedTargetId);
            }
        }

        var ignoredGroupIds = new Dictionary<string, string>();
        var pinnedUnsorted = new HashSet<string>();
        foreach (var (identityId, decision) in latest)
        {
            if (decision.Kind == PhotoAnalysisDecisionKind.Ignored && decision.ChosenTargetId is not null)
                ignoredGroupIds[identityId] = decision.ChosenTargetId;
            else if (decision.Kind is PhotoAnalysisDecisionKind.Rejected or PhotoAnalysisDecisionKind.Merged)
                pinnedUnsorted.Add(identityId);
        }

        return new FaceIdentityReviewStates(
            settled,
            ignoredGroupIds,
            pinnedUnsorted,
            negatives.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value));
    }

    public static async Task<IReadOnlySet<string>> SettledIdsAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken) =>
        (await LoadAsync(database, cancellationToken)).SettledIds;
}

public sealed record FaceIdentityReviewStates(
    IReadOnlySet<string> SettledIds,
    IReadOnlyDictionary<string, string> IgnoredGroupIds,
    IReadOnlySet<string> PinnedUnsortedIds,
    IReadOnlyDictionary<string, IReadOnlySet<string>> NegativePersonIds);
