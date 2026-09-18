namespace KnowledgeBase.Api.Controllers.PhotoAnalysis.Contracts;

public sealed record PhotoAnalysisResponse(
    IReadOnlyList<PersonResponse> People,
    IReadOnlyList<LocationResponse> Locations,
    IReadOnlyList<ArchiveEventResponse> Events,
    IReadOnlyList<PhotoAnalysisCandidateResponse> PendingCandidates,
    IReadOnlyList<SceneObservationResponse> PendingSceneObservations,
    IReadOnlyList<EventCandidateResponse> PendingEventCandidates,
    FaceAnalysisStatusResponse FaceAnalysisStatus,
    SceneAnalysisStatusResponse SceneAnalysisStatus,
    ObservationAnalysisStatusResponse ObservationAnalysisStatus,
    EventAnalysisStatusResponse EventAnalysisStatus);

public sealed record PersonResponse(string Id, string Name, int EventCount, IReadOnlyList<PersonReferenceFaceResponse> ReferenceFaces);
public sealed record PersonReferenceFaceResponse(string Id, string AssetId, string AssetName, FaceBoundsResponse FaceBounds, DateTime ConfirmedAtUtc);
public sealed record LocationReferencePhotoResponse(string AssetId, string AssetName, DateTime ConfirmedAtUtc);
public sealed record LocationResponse(string Id, string Name, string Kind, int EventCount, int ConfirmedPhotoCount, IReadOnlyList<LocationReferencePhotoResponse> ReferencePhotos);
public sealed record ArchiveEventResponse(
    string Id,
    string Title,
    DateOnly? OccurredOn,
    string? LocationId,
    string? LocationName,
    IReadOnlyList<string> PersonIds,
    IReadOnlyList<string> AssetIds);

public sealed record PhotoAnalysisCandidateResponse(
    string Id,
    string Kind,
    string SubjectAssetId,
    string SubjectAssetName,
    string? SubjectFaceOccurrenceId,
    string? ProposedTargetId,
    string? ProposedTargetName,
    string? ProposedLabel,
    int Rank,
    // Score is the raw stored measure; Confidence is the calibrated 0-1 form the review screen shows.
    double Score,
    double Confidence,
    string SignalsJson,
    string RunId,
    FaceBoundsResponse? FaceBounds,
    IReadOnlyList<PhotoAnalysisCandidateMatchResponse> Matches);

/// <summary>What this subject scored against one target in the same run, for every ranked target - not only the best one.</summary>
public sealed record PhotoAnalysisCandidateMatchResponse(string TargetId, double Score, double Confidence);

public sealed record FaceBoundsResponse(int X, int Y, int Width, int Height);

public sealed record SceneObservationResponse(
    string Id,
    string AssetId,
    string AssetName,
    string Kind,
    string? SubjectPersonName,
    string? RelatedPersonName,
    string Description,
    string Evidence,
    double Confidence,
    string RunId);

public sealed record EventCandidateResponse(
    string Id,
    double Score,
    DateOnly? SuggestedOccurredOn,
    string SignalsJson,
    IReadOnlyList<EventCandidatePhotoResponse> Photos);
public sealed record EventCandidatePhotoResponse(string Id, string Name);

public sealed record PhotoAnalysisReviewDecisionResponse(
    string Id,
    string CandidateId,
    string Kind,
    string? ChosenTargetId,
    string? Note,
    DateTime DecidedAtUtc);
public sealed record FaceAnalysisStatusResponse(int ImagesWithoutFingerprint, int PendingFingerprintJobs, int PendingFaceJobs);
public sealed record FaceAnalysisBatchResponse(int FingerprintsQueued, int FaceAnalysesQueued);
public sealed record SceneAnalysisStatusResponse(int PendingSceneJobs);
public sealed record SceneAnalysisBatchResponse(int FingerprintsQueued, int SceneAnalysesQueued);
public sealed record ObservationAnalysisStatusResponse(int PendingObservationJobs);
public sealed record ObservationAnalysisBatchResponse(int FingerprintsQueued, int ObservationAnalysesQueued);
public sealed record EventAnalysisStatusResponse(int PendingEventJobs);
public sealed record EventAnalysisBatchResponse(int FingerprintsQueued, bool EventAnalysisQueued);

public sealed record CreatePersonRequest(string Name);
public sealed record CreateLocationRequest(string Name, string Kind);
public sealed record CreateArchiveEventRequest(
    string Title,
    DateOnly? OccurredOn,
    string? LocationId,
    IReadOnlyList<string>? PersonIds,
    IReadOnlyList<string>? AssetIds);
public sealed record CreatePhotoAnalysisReviewDecisionRequest(string Kind, string? ChosenTargetId, string? Note);
public sealed record CreateSceneObservationReviewDecisionRequest(string Kind, string? Note);
public sealed record CreateEventCandidateReviewDecisionRequest(
    string Kind,
    string? ChosenEventId,
    string? Title,
    DateOnly? OccurredOn,
    string? LocationId,
    IReadOnlyList<string>? PersonIds,
    IReadOnlyList<string>? AssetIds,
    string? Note);
