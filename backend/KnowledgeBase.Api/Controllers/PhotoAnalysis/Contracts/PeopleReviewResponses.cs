namespace KnowledgeBase.Api.Controllers.PhotoAnalysis.Contracts;

public sealed record PeopleReviewResponse(
    bool ClusteringPending,
    IReadOnlyList<PeopleReviewPersonRowResponse> PersonRows,
    IReadOnlyList<PeopleReviewAnonymousRowResponse> AnonymousRows,
    IReadOnlyList<PeopleReviewFaceResponse> Unsorted,
    IReadOnlyList<PeopleReviewIgnoredGroupResponse> IgnoredGroups);

public sealed record PeopleReviewPersonRowResponse(
    string PersonId,
    string Name,
    IReadOnlyList<PersonReferenceFaceResponse> ReferenceFaces,
    int ReferenceFaceCount,
    IReadOnlyList<PeopleReviewFaceResponse> Faces);

public sealed record PeopleReviewAnonymousRowResponse(string ClusterId, PeopleReviewHintResponse? Hint, IReadOnlyList<PeopleReviewFaceResponse> Faces);

public sealed record PeopleReviewIgnoredGroupResponse(string GroupId, PeopleReviewHintResponse? Hint, IReadOnlyList<PeopleReviewFaceResponse> Faces);

public sealed record PeopleReviewHintResponse(string PersonId, string Name, double Score);

// Score is the in-row order score (raw cosine): to the person for a person row, to the other
// members for a group, zero for an unsorted face.
public sealed record PeopleReviewFaceResponse(string CandidateId, string FaceOccurrenceId, string AssetId, FaceBoundsResponse FaceBounds, double Score);

public sealed record SubmitPeopleReviewRequest(IReadOnlyList<string>? CandidateIds, IReadOnlyList<string>? RemovedCandidateIds, string? PersonId, string? Name);

public sealed record IgnorePeopleReviewRequest(IReadOnlyList<string>? CandidateIds, IReadOnlyList<string>? RemovedCandidateIds);

public sealed record SubmitPeopleReviewResponse(string PersonId, string Name, bool Created);
