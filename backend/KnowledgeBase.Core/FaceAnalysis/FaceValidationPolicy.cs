using KnowledgeBase.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.FaceAnalysis;

public static class FaceValidationPolicy
{
    public const string PipelineVersion = "face-validation/v1";

    public static FaceValidationAssessment Assess(FaceOccurrence face, FaceValidation? validation, FaceValidationDecisionKind? decision)
    {
        var reasons = new List<string>();
        if (face.IsPartial || validation?.TouchesImageEdge == true) reasons.Add("ImageEdge");
        if (face.NeedsReview) reasons.Add("DetectorUnconfirmed");
        if (validation?.MinSidePixels is < 32) reasons.Add("SmallCrop");
        if (validation?.Sharpness112 is < 10) reasons.Add("LowSharpness");
        if (decision is FaceValidationDecisionKind.Approved)
            return new("Approved", true, reasons);
        if (decision is FaceValidationDecisionKind.Excluded)
            return new("Excluded", false, reasons);
        if (validation?.PipelineVersion != PipelineVersion || validation.CompletedAtUtc is null || validation.Subject is null)
            return new("Pending", false, reasons);
        if (validation.Subject != FaceValidationSubject.HumanFace) reasons.Add(validation.Subject.Value.ToString());
        var eligible = validation.Subject == FaceValidationSubject.HumanFace && !face.NeedsReview;
        return new(eligible ? "Eligible" : "NeedsReview", eligible, reasons);
    }

    public static async Task<FaceValidationState> LoadAsync(KnowledgeBaseDbContext database, CancellationToken cancellationToken)
    {
        var validations = await database.FaceValidations.AsNoTracking().ToDictionaryAsync(item => item.FaceOccurrenceId, cancellationToken);
        var decisions = (await database.FaceValidationReviewDecisions.AsNoTracking()
            .OrderBy(item => item.DecidedAtUtc).ThenBy(item => item.Id).ToListAsync(cancellationToken))
            .GroupBy(item => item.FaceIdentityId).ToDictionary(group => group.Key, group => group.Last().Kind);
        return new(validations, decisions);
    }
}

public sealed record FaceValidationAssessment(string Status, bool CanUseForPeople, IReadOnlyList<string> Reasons);

public sealed record FaceValidationState(
    IReadOnlyDictionary<string, FaceValidation> Validations,
    IReadOnlyDictionary<string, FaceValidationDecisionKind> Decisions)
{
    public FaceValidationAssessment Assess(FaceOccurrence face) => FaceValidationPolicy.Assess(face,
        Validations.GetValueOrDefault(face.Id),
        face.IdentityId is { } identity && Decisions.TryGetValue(identity, out var decision) ? decision : null);
}
