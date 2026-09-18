using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.FaceAnalysis;

// Which faces a human has already disposed of, resolved through the identity rather than any one
// occurrence row: a re-detection stores a new occurrence for the same physical face, and its
// settled state must follow the face. A reference face settles the identity (accepted or
// corrected person proposals create one); a rejected or merged proposal settles it without one.
public static class FaceIdentityState
{
    public static async Task<HashSet<string>> SettledIdsAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var settled = (await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            where occurrence.IdentityId != null
            select occurrence.IdentityId!).ToListAsync(cancellationToken)).ToHashSet();
        settled.UnionWith(await (
            from candidate in database.PhotoAnalysisCandidates
            join decision in database.PhotoAnalysisReviewDecisions on candidate.Id equals decision.CandidateId
            where (decision.Kind == PhotoAnalysisDecisionKind.Rejected || decision.Kind == PhotoAnalysisDecisionKind.Merged)
                && candidate.SubjectFaceOccurrenceId != null
            join occurrence in database.FaceOccurrences on candidate.SubjectFaceOccurrenceId equals occurrence.Id
            where occurrence.IdentityId != null
            select occurrence.IdentityId!).ToListAsync(cancellationToken));
        return settled;
    }
}
