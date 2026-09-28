using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.SceneAnalysis;

/// <summary>
/// Review states derived from existing human decisions on location proposals,
/// analogous to FaceIdentityReviewStates for faces.
/// </summary>
public sealed class SceneIdentityReviewStates
{
    public required IReadOnlySet<string> SettledIds { get; init; }
    public required IReadOnlySet<string> PinnedUnsortedIds { get; init; }
    public required IReadOnlyDictionary<string, string> ExcludedGroupIds { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlySet<string>> NegativeLocationIds { get; init; }

    public static async Task<SceneIdentityReviewStates> LoadAsync(
        KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var settled = (await (
            from obs in database.LocationObservations
            join si in database.SceneIdentities on obs.AssetId equals si.AssetId
            select si.Id
        ).ToListAsync(cancellationToken)).ToHashSet();

        var rows = await (
            from decision in database.PhotoAnalysisReviewDecisions
            join candidate in database.PhotoAnalysisCandidates on decision.CandidateId equals candidate.Id
            join si in database.SceneIdentities on candidate.SubjectAssetId equals si.AssetId
            where candidate.Kind == PhotoAnalysisCandidateKind.Location
            orderby decision.DecidedAtUtc, decision.Id
            select new { IdentityId = si.Id, decision.Kind, decision.ChosenTargetId, candidate.ProposedTargetId }
        ).ToListAsync(cancellationToken);

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

        var excludedGroupIds = new Dictionary<string, string>();
        var pinnedUnsorted = new HashSet<string>();
        foreach (var (identityId, decision) in latest)
        {
            if (decision.Kind == PhotoAnalysisDecisionKind.Ignored && decision.ChosenTargetId is not null)
                excludedGroupIds[identityId] = decision.ChosenTargetId;
            else if (decision.Kind is PhotoAnalysisDecisionKind.Rejected or PhotoAnalysisDecisionKind.Merged)
                pinnedUnsorted.Add(identityId);
        }

        return new SceneIdentityReviewStates
        {
            SettledIds = settled,
            PinnedUnsortedIds = pinnedUnsorted,
            ExcludedGroupIds = excludedGroupIds,
            NegativeLocationIds = negatives.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value)
        };
    }

    public static async Task<IReadOnlySet<string>> SettledIdsAsync(
        KnowledgeBaseDbContext database, CancellationToken cancellationToken) =>
        (await LoadAsync(database, cancellationToken)).SettledIds;
}
