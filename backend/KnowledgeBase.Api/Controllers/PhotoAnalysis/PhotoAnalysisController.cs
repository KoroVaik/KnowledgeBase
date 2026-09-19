using KnowledgeBase.Api.Controllers.PhotoAnalysis.Contracts;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.EventClustering;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KnowledgeBase.Api.Controllers.PhotoAnalysis;

/// <summary>Manually curated people, locations and events for the photo archive.</summary>
[ApiController]
[Route("api/photo-analysis")]
[Authorize]
[Produces("application/json")]
public sealed class PhotoAnalysisController(KnowledgeBaseDbContext database, IChangeNotifier notifier, IOptions<FaceConfidenceCalibration> faceCalibration) : ControllerBase
{
    // A face score is a cosine between embeddings; person candidates show its calibrated form.
    // Every other candidate kind has its own score scale, so it is shown as stored.
    private double ConfidenceFor(PhotoAnalysisCandidate candidate) =>
        candidate.Kind == PhotoAnalysisCandidateKind.Person
            ? faceCalibration.Value.ToConfidence(candidate.Score)
            : candidate.Score;
    [HttpGet]
    public async Task<ActionResult<PhotoAnalysisResponse>> List(CancellationToken cancellationToken)
    {
        var people = await database.People.OrderBy(person => person.Name).ToListAsync(cancellationToken);
        var locations = await database.Locations.OrderBy(location => location.Name).ToListAsync(cancellationToken);
        var events = await database.ArchiveEvents.OrderByDescending(@event => @event.OccurredOn).ThenBy(@event => @event.Title).ToListAsync(cancellationToken);
        var eventPeople = await database.ArchiveEventPeople.ToListAsync(cancellationToken);
        var eventPhotos = await database.ArchiveEventPhotos.ToListAsync(cancellationToken);
        var assets = await database.Assets.ToListAsync(cancellationToken);
        var canonicalAssetIds = assets
            .GroupBy(asset => asset.ContentSha256 ?? asset.Id)
            .Select(group => group.OrderBy(asset => asset.UploadedAtUtc).ThenBy(asset => asset.Id).First().Id)
            .ToHashSet();
        // The worker stores one ranked guess per possible target per photo, but the reviewer decides
        // about the photo, not about each guess in turn. Only the best open guess per subject is offered
        // here; the rest stay in the database as evidence. Person candidates are not listed here at all:
        // faces are reviewed as rows of people in PeopleReviewController.
        var confirmedLocationAssetIds = (await database.LocationObservations
            .Select(observation => observation.AssetId).ToListAsync(cancellationToken)).ToHashSet();
        var rejectedSubjects = (await (
            from candidate in database.PhotoAnalysisCandidates
            join decision in database.PhotoAnalysisReviewDecisions on candidate.Id equals decision.CandidateId
            where decision.Kind == PhotoAnalysisDecisionKind.Rejected || decision.Kind == PhotoAnalysisDecisionKind.Merged
            select new { candidate.Kind, candidate.SubjectAssetId, candidate.SubjectFaceOccurrenceId }).ToListAsync(cancellationToken))
            .Select(candidate => ReviewSubjectKey(candidate.Kind, candidate.SubjectAssetId, candidate.SubjectFaceOccurrenceId))
            .ToHashSet();
        var pendingSubjects = (await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.Kind != PhotoAnalysisCandidateKind.Person && candidate.SupersededAtUtc == null
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ToListAsync(cancellationToken))
            .Where(candidate => canonicalAssetIds.Contains(candidate.SubjectAssetId)
                && (candidate.Kind != PhotoAnalysisCandidateKind.Location || !confirmedLocationAssetIds.Contains(candidate.SubjectAssetId))
                && !rejectedSubjects.Contains(ReviewSubjectKey(candidate.Kind, candidate.SubjectAssetId, candidate.SubjectFaceOccurrenceId)))
            .GroupBy(candidate => ReviewSubjectKey(candidate.Kind, candidate.SubjectAssetId, candidate.SubjectFaceOccurrenceId))
            .Select(group =>
            {
                var best = group.OrderByDescending(candidate => candidate.Score).ThenByDescending(candidate => candidate.CreatedAtUtc).First();
                // The weaker guesses are not separate review items, but they carry this subject's score for
                // every other target - which is what the "choose existing" picker shows.
                var matches = group
                    .Where(candidate => candidate.ProposedTargetId is not null)
                    .GroupBy(candidate => candidate.ProposedTargetId!)
                    .Select(targetGroup => targetGroup.OrderByDescending(candidate => candidate.CreatedAtUtc).First())
                    .OrderByDescending(candidate => candidate.Score)
                    .Select(candidate => new PhotoAnalysisCandidateMatchResponse(candidate.ProposedTargetId!, candidate.Score, ConfidenceFor(candidate)))
                    .ToList();
                // Ordered by when the subject first came up for review, not by its newest row: a refresh
                // must not shuffle the queue the reviewer is working through.
                return (Best: best, Matches: matches, FirstSeenAtUtc: group.Min(candidate => candidate.CreatedAtUtc));
            })
            .OrderBy(subject => subject.FirstSeenAtUtc).ThenByDescending(subject => subject.Best.Score).ToList();
        var pendingCandidates = pendingSubjects.Select(subject => subject.Best).ToList();
        var faceOccurrenceIds = pendingCandidates
            .Where(candidate => candidate.SubjectFaceOccurrenceId is not null)
            .Select(candidate => candidate.SubjectFaceOccurrenceId!)
            .ToHashSet();
        var faceBoundsByOccurrenceId = (await database.FaceOccurrences
            .Where(occurrence => faceOccurrenceIds.Contains(occurrence.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(occurrence => occurrence.Id, occurrence => new FaceBoundsResponse(occurrence.X, occurrence.Y, occurrence.Width, occurrence.Height));
        var pendingSceneObservations = (await database.SceneObservations
            .Where(observation => observation.SupersededAtUtc == null
                && !database.SceneObservationReviewDecisions.Any(decision => decision.ObservationId == observation.Id))
            .OrderBy(observation => observation.CreatedAtUtc)
            .ToListAsync(cancellationToken)).Where(observation => canonicalAssetIds.Contains(observation.AssetId)).ToList();
        var pendingEventCandidates = await database.EventCandidates
            .Where(candidate => candidate.SupersededAtUtc == null
                && !database.EventCandidateReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .OrderBy(candidate => candidate.CreatedAtUtc).ToListAsync(cancellationToken);
        var eventCandidateClusterIds = pendingEventCandidates.Select(candidate => candidate.ClusterId).ToHashSet();
        var clustersById = (await database.EventClusters.Where(cluster => eventCandidateClusterIds.Contains(cluster.Id)).ToListAsync(cancellationToken))
            .ToDictionary(cluster => cluster.Id);
        var eventCandidatePhotos = (await database.EventClusterPhotos.Where(photo => eventCandidateClusterIds.Contains(photo.ClusterId)).ToListAsync(cancellationToken))
            .GroupBy(photo => photo.ClusterId).ToDictionary(group => group.Key, group => group.Select(photo => photo.AssetId).ToList());
        var candidateAssets = assets.ToDictionary(asset => asset.Id);
        var imageAssetIds = assets
            .Where(asset => ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is ContentKind.Image)
            .Select(asset => asset.Id).ToHashSet();
        var activeJobs = await database.ProcessingJobs
            .Where(job => imageAssetIds.Contains(job.AssetId!) && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running))
            .ToListAsync(cancellationToken);
        var pendingEventJobs = await database.ProcessingJobs.CountAsync(
            job => job.Kind == JobKind.AnalyzeEventCandidates
                && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running), cancellationToken);

        var referenceFaces = await database.PersonReferenceFaces.ToListAsync(cancellationToken);
        var referenceFaceOccurrences = (await database.FaceOccurrences
            .Where(occurrence => referenceFaces.Select(reference => reference.FaceOccurrenceId).Contains(occurrence.Id))
            .ToListAsync(cancellationToken)).ToDictionary(occurrence => occurrence.Id);
        var referenceFacesByPerson = referenceFaces
            .Where(reference => referenceFaceOccurrences.TryGetValue(reference.FaceOccurrenceId, out var occurrence) && candidateAssets.ContainsKey(occurrence.AssetId))
            .OrderByDescending(reference => reference.ConfirmedAtUtc)
            .GroupBy(reference => reference.PersonId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var personCounts = eventPeople.GroupBy(link => link.PersonId).ToDictionary(group => group.Key, group => group.Count());
        var locationCounts = events.Where(@event => @event.LocationId is not null).GroupBy(@event => @event.LocationId!).ToDictionary(group => group.Key, group => group.Count());
        var locationObservations = await database.LocationObservations.ToListAsync(cancellationToken);
        var locationPhotoCounts = locationObservations
            .GroupBy(observation => observation.LocationId).ToDictionary(group => group.Key, group => group.Count());
        var referencePhotosByLocation = locationObservations
            .Where(observation => candidateAssets.ContainsKey(observation.AssetId))
            .OrderByDescending(observation => observation.ConfirmedAtUtc)
            .GroupBy(observation => observation.LocationId)
            .ToDictionary(group => group.Key, group => group.Select(observation =>
                new LocationReferencePhotoResponse(observation.AssetId, candidateAssets[observation.AssetId].OriginalFileName, observation.ConfirmedAtUtc)).ToList());
        var locationsById = locations.ToDictionary(location => location.Id);

        return Ok(new PhotoAnalysisResponse(
            people.Select(person => new PersonResponse(
                person.Id, person.Name, personCounts.GetValueOrDefault(person.Id),
                referenceFacesByPerson.GetValueOrDefault(person.Id, [])
                    .Select(reference =>
                    {
                        var occurrence = referenceFaceOccurrences[reference.FaceOccurrenceId];
                        return new PersonReferenceFaceResponse(
                            reference.FaceOccurrenceId, occurrence.AssetId, candidateAssets[occurrence.AssetId].OriginalFileName,
                            new FaceBoundsResponse(occurrence.X, occurrence.Y, occurrence.Width, occurrence.Height),
                            reference.ConfirmedAtUtc);
                    }).ToList())).ToList(),
            locations.Select(location => new LocationResponse(location.Id, location.Name, location.Kind.ToString(), locationCounts.GetValueOrDefault(location.Id), locationPhotoCounts.GetValueOrDefault(location.Id), referencePhotosByLocation.GetValueOrDefault(location.Id, []))).ToList(),
            events.Select(@event => new ArchiveEventResponse(
                @event.Id, @event.Title, @event.OccurredOn, @event.LocationId,
                @event.LocationId is { } locationId ? locationsById.GetValueOrDefault(locationId)?.Name : null,
                eventPeople.Where(link => link.EventId == @event.Id).Select(link => link.PersonId).ToList(),
                eventPhotos.Where(link => link.EventId == @event.Id).Select(link => link.AssetId).ToList())).ToList(),
            pendingSubjects.Select(subject => new PhotoAnalysisCandidateResponse(
                subject.Best.Id,
                subject.Best.Kind.ToString(),
                subject.Best.SubjectAssetId,
                candidateAssets.GetValueOrDefault(subject.Best.SubjectAssetId)?.OriginalFileName ?? "Deleted photo",
                subject.Best.SubjectFaceOccurrenceId,
                subject.Best.ProposedTargetId,
                CandidateTargetName(subject.Best, people, locations, events),
                subject.Best.ProposedLabel,
                subject.Best.Rank,
                subject.Best.Score,
                ConfidenceFor(subject.Best),
                subject.Best.SignalsJson,
                subject.Best.RunId,
                subject.Best.SubjectFaceOccurrenceId is { } faceOccurrenceId
                    ? faceBoundsByOccurrenceId.GetValueOrDefault(faceOccurrenceId)
                    : null,
                subject.Matches)).ToList(),
            pendingSceneObservations.Select(observation => new SceneObservationResponse(
                observation.Id, observation.AssetId,
                candidateAssets.GetValueOrDefault(observation.AssetId)?.OriginalFileName ?? "Deleted photo",
                observation.Kind.ToString(), observation.SubjectPersonName, observation.RelatedPersonName,
                observation.Description, observation.Evidence, observation.Confidence, observation.RunId)).ToList(),
            pendingEventCandidates.Select(candidate => new EventCandidateResponse(
                candidate.Id, candidate.Score, candidate.SuggestedOccurredOn,
                clustersById.GetValueOrDefault(candidate.ClusterId)?.SignalsJson ?? "{}",
                eventCandidatePhotos.GetValueOrDefault(candidate.ClusterId, [])
                    .Select(assetId => new EventCandidatePhotoResponse(assetId, candidateAssets.GetValueOrDefault(assetId)?.OriginalFileName ?? "Deleted photo")).ToList())).ToList(),
            new FaceAnalysisStatusResponse(
                assets.Count(asset => imageAssetIds.Contains(asset.Id) && asset.ContentSha256 is null),
                activeJobs.Count(job => job.Kind == JobKind.FingerprintAsset),
                activeJobs.Count(job => job.Kind == JobKind.AnalyzeFaces)),
            new SceneAnalysisStatusResponse(activeJobs.Count(job => job.Kind == JobKind.AnalyzeScenes)),
            new ObservationAnalysisStatusResponse(activeJobs.Count(job => job.Kind == JobKind.AnalyzeSceneObservations)),
            new EventAnalysisStatusResponse(pendingEventJobs)));
    }

    [HttpPost("people")]
    public async Task<ActionResult<PersonResponse>> CreatePerson([FromBody] CreatePersonRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "A person needs a name." });
        if (await database.People.AnyAsync(person => person.Name.ToLower() == name.ToLower(), cancellationToken))
            return Conflict(new { error = $"A person named “{name}” already exists." });

        var person = new Person { Id = Guid.NewGuid().ToString("N"), Name = name, CreatedAtUtc = DateTime.UtcNow };
        database.People.Add(person);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Created(string.Empty, new PersonResponse(person.Id, person.Name, 0, []));
    }

    /// <summary>Undoes an accepted/corrected face by removing the reference face it created. The face is no
    /// longer settled, so the next face grouping puts it back into people review; the original decision stays
    /// untouched, so the audit trail of what was decided (and now revoked) is preserved.</summary>
    [HttpDelete("people/{personId}/reference-faces/{faceOccurrenceId}")]
    public async Task<ActionResult> RevokeReferenceFace(string personId, string faceOccurrenceId, CancellationToken cancellationToken)
    {
        var reference = await database.PersonReferenceFaces.SingleOrDefaultAsync(
            item => item.PersonId == personId && item.FaceOccurrenceId == faceOccurrenceId, cancellationToken);
        if (reference is null) return NotFound();

        database.PersonReferenceFaces.Remove(reference);
        await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    [HttpPost("locations")]
    public async Task<ActionResult<LocationResponse>> CreateLocation([FromBody] CreateLocationRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "A location needs a name." });
        if (!Enum.TryParse<LocationKind>(request.Kind, true, out var kind)) return BadRequest(new { error = "Location kind must be Physical or Visual." });
        if (await database.Locations.AnyAsync(location => location.Name.ToLower() == name.ToLower() && location.Kind == kind, cancellationToken))
            return Conflict(new { error = $"That {kind.ToString().ToLowerInvariant()} location already exists." });

        var location = new Location { Id = Guid.NewGuid().ToString("N"), Name = name, Kind = kind, CreatedAtUtc = DateTime.UtcNow };
        database.Locations.Add(location);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Created(string.Empty, new LocationResponse(location.Id, location.Name, location.Kind.ToString(), 0, 0, []));
    }

    /// <summary>Undoes an accepted/corrected location candidate by removing the observation it created and
    /// re-opening the same proposal for review; the original decision stays in the audit trail.</summary>
    [HttpDelete("locations/{locationId}/reference-photos/{assetId}")]
    public async Task<ActionResult> RevokeLocationPhoto(string locationId, string assetId, CancellationToken cancellationToken)
    {
        var observation = await database.LocationObservations.SingleOrDefaultAsync(
            item => item.LocationId == locationId && item.AssetId == assetId, cancellationToken);
        if (observation is null) return NotFound();

        database.LocationObservations.Remove(observation);
        await ReopenReviewedCandidate(observation.SourceDecisionId, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    [HttpPost("events")]
    public async Task<ActionResult<ArchiveEventResponse>> CreateEvent([FromBody] CreateArchiveEventRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title.Trim();
        if (title.Length == 0) return BadRequest(new { error = "An event needs a title." });
        var personIds = request.PersonIds?.Distinct().ToList() ?? [];
        var assetIds = request.AssetIds?.Distinct().ToList() ?? [];
        if (request.LocationId is not null && !await database.Locations.AnyAsync(location => location.Id == request.LocationId, cancellationToken)) return BadRequest(new { error = "The selected location no longer exists." });
        if (await database.People.CountAsync(person => personIds.Contains(person.Id), cancellationToken) != personIds.Count) return BadRequest(new { error = "One of the selected people no longer exists." });
        if (await database.Assets.CountAsync(asset => assetIds.Contains(asset.Id), cancellationToken) != assetIds.Count) return BadRequest(new { error = "One of the selected photos no longer exists." });

        var @event = new ArchiveEvent { Id = Guid.NewGuid().ToString("N"), Title = title, OccurredOn = request.OccurredOn, LocationId = request.LocationId, CreatedAtUtc = DateTime.UtcNow };
        database.ArchiveEvents.Add(@event);
        database.ArchiveEventPeople.AddRange(personIds.Select(personId => new ArchiveEventPerson { EventId = @event.Id, PersonId = personId }));
        database.ArchiveEventPhotos.AddRange(assetIds.Select(assetId => new ArchiveEventPhoto { EventId = @event.Id, AssetId = assetId }));
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Created(string.Empty, new ArchiveEventResponse(@event.Id, @event.Title, @event.OccurredOn, @event.LocationId, null, personIds, assetIds));
    }

    /// <summary>Detaches one photo from a curated event. Unlike a person or location reference, an event photo
    /// carries no link back to a candidate (an event is assembled by hand, or from a whole cluster at once),
    /// so nothing is re-opened for review.</summary>
    [HttpDelete("events/{eventId}/photos/{assetId}")]
    public async Task<ActionResult> DetachEventPhoto(string eventId, string assetId, CancellationToken cancellationToken)
    {
        var photo = await database.ArchiveEventPhotos.SingleOrDefaultAsync(
            item => item.EventId == eventId && item.AssetId == assetId, cancellationToken);
        if (photo is null) return NotFound();

        database.ArchiveEventPhotos.Remove(photo);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    /// <summary>Queues face analysis for existing images, first fingerprinting them to skip exact duplicate bytes.</summary>
    [HttpPost("analyze-faces")]
    public async Task<ActionResult<FaceAnalysisBatchResponse>> QueueFaceAnalysis(CancellationToken cancellationToken)
    {
        var images = (await database.Assets.ToListAsync(cancellationToken))
            .Where(asset => ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is ContentKind.Image)
            .ToList();
        var fingerprintsQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is null))
        {
            if (await ProcessingQueue.EnsurePendingAsync(database, image.Id, JobKind.FingerprintAsset, cancellationToken)) fingerprintsQueued++;
        }

        var faceAnalysesQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is not null)
                     .GroupBy(image => image.ContentSha256!)
                     .Select(group => group.OrderBy(image => image.UploadedAtUtc).ThenBy(image => image.Id).First()))
        {
            if (await ProcessingQueue.EnsureQueuedAsync(database, image.Id, JobKind.AnalyzeFaces, cancellationToken)) faceAnalysesQueued++;
        }

        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Accepted(new FaceAnalysisBatchResponse(fingerprintsQueued, faceAnalysesQueued));
    }

    /// <summary>Refreshes CLIP location suggestions for every unconfirmed canonical image, first fingerprinting new images to skip exact duplicate bytes.</summary>
    [HttpPost("analyze-scenes")]
    public async Task<ActionResult<SceneAnalysisBatchResponse>> QueueSceneAnalysis(CancellationToken cancellationToken)
    {
        var images = (await database.Assets.ToListAsync(cancellationToken))
            .Where(asset => ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is ContentKind.Image)
            .ToList();
        var fingerprintsQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is null))
        {
            if (await ProcessingQueue.EnsureQueuedAsync(database, image.Id, JobKind.FingerprintAsset, cancellationToken)) fingerprintsQueued++;
        }

        var confirmedLocationAssetIds = (await database.LocationObservations.ToListAsync(cancellationToken))
            .Select(observation => observation.AssetId).ToHashSet();
        var sceneAnalysesQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is not null)
                     .GroupBy(image => image.ContentSha256!)
                     .Select(group => group.OrderBy(image => image.UploadedAtUtc).ThenBy(image => image.Id).First())
                     .Where(image => !confirmedLocationAssetIds.Contains(image.Id)))
        {
            if (await ProcessingQueue.EnsurePendingAsync(database, image.Id, JobKind.AnalyzeScenes, cancellationToken)) sceneAnalysesQueued++;
        }

        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Accepted(new SceneAnalysisBatchResponse(fingerprintsQueued, sceneAnalysesQueued));
    }

    /// <summary>Refreshes VLM scene observations for canonical photos with reviewed person or location context.</summary>
    [HttpPost("analyze-observations")]
    public async Task<ActionResult<ObservationAnalysisBatchResponse>> QueueSceneObservations(CancellationToken cancellationToken)
    {
        var images = (await database.Assets.ToListAsync(cancellationToken))
            .Where(asset => ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is ContentKind.Image)
            .ToList();
        var fingerprintsQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is null))
        {
            if (await ProcessingQueue.EnsureQueuedAsync(database, image.Id, JobKind.FingerprintAsset, cancellationToken)) fingerprintsQueued++;
        }

        var personAssetIds = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            select occurrence.AssetId).ToListAsync(cancellationToken);
        var locationAssetIds = await database.LocationObservations.Select(observation => observation.AssetId).ToListAsync(cancellationToken);
        var contextualAssetIds = personAssetIds.Concat(locationAssetIds).ToHashSet();
        var observationAnalysesQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is not null)
                     .GroupBy(image => image.ContentSha256!)
                     .Select(group => group.OrderBy(image => image.UploadedAtUtc).ThenBy(image => image.Id).First())
                     .Where(image => contextualAssetIds.Contains(image.Id)))
        {
            if (await ProcessingQueue.EnsurePendingAsync(database, image.Id, JobKind.AnalyzeSceneObservations, cancellationToken)) observationAnalysesQueued++;
        }

        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Accepted(new ObservationAnalysisBatchResponse(fingerprintsQueued, observationAnalysesQueued));
    }

    /// <summary>Queues reviewed-evidence clustering into possible events, after fingerprinting new images.</summary>
    [HttpPost("analyze-events")]
    public async Task<ActionResult<EventAnalysisBatchResponse>> QueueEventAnalysis(CancellationToken cancellationToken)
    {
        var images = (await database.Assets.ToListAsync(cancellationToken))
            .Where(asset => ProcessableContent.Classify(asset.ContentType, asset.OriginalFileName) is ContentKind.Image)
            .ToList();
        var fingerprintsQueued = 0;
        foreach (var image in images.Where(image => image.ContentSha256 is null))
        {
            if (await ProcessingQueue.EnsureQueuedAsync(database, image.Id, JobKind.FingerprintAsset, cancellationToken)) fingerprintsQueued++;
        }
        var eventAnalysisQueued = fingerprintsQueued == 0 && await EventCandidateQueue.EnqueueAsync(database, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Accepted(new EventAnalysisBatchResponse(fingerprintsQueued, eventAnalysisQueued));
    }

    /// <summary>Records the user's final review of one model candidate without changing the original model evidence.
    /// The subject is settled by that one answer, so its other pending proposals are superseded rather than left in review.</summary>
    [HttpPost("candidates/{candidateId}/decisions")]
    public async Task<ActionResult<PhotoAnalysisReviewDecisionResponse>> CreateReviewDecision(string candidateId, [FromBody] CreatePhotoAnalysisReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        var candidate = await database.PhotoAnalysisCandidates.SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null) return NotFound();
        if (await database.PhotoAnalysisReviewDecisions.AnyAsync(item => item.CandidateId == candidateId, cancellationToken))
            return Conflict(new { error = "This candidate has already been reviewed." });
        // Ignored files a face into an ignored group, which only people review creates.
        if (!Enum.TryParse<PhotoAnalysisDecisionKind>(request.Kind, true, out var kind) || kind == PhotoAnalysisDecisionKind.Ignored)
            return BadRequest(new { error = "Decision kind must be Accepted, Rejected, Corrected or Merged." });

        var chosenTargetId = kind == PhotoAnalysisDecisionKind.Accepted ? candidate.ProposedTargetId : request.ChosenTargetId;
        if (kind == PhotoAnalysisDecisionKind.Rejected && chosenTargetId is not null)
            return BadRequest(new { error = "A rejected candidate cannot have a chosen target." });
        if (kind is PhotoAnalysisDecisionKind.Corrected or PhotoAnalysisDecisionKind.Merged && string.IsNullOrWhiteSpace(chosenTargetId))
            return BadRequest(new { error = "A corrected or merged candidate needs a chosen target." });
        if (kind == PhotoAnalysisDecisionKind.Accepted && chosenTargetId is null)
            return BadRequest(new { error = "This proposal has no existing target to accept. Create one, then correct the candidate to it." });
        if (chosenTargetId is not null && !await TargetExists(candidate.Kind, chosenTargetId, cancellationToken))
            return BadRequest(new { error = "The chosen archive record no longer exists." });

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 1000) return BadRequest(new { error = "The review note is limited to 1000 characters." });
        VisualEmbedding? visualEmbedding = null;
        if (candidate.Kind == PhotoAnalysisCandidateKind.Location && kind is PhotoAnalysisDecisionKind.Accepted or PhotoAnalysisDecisionKind.Corrected)
        {
            visualEmbedding = await database.VisualEmbeddings.SingleOrDefaultAsync(
                embedding => embedding.RunId == candidate.RunId && embedding.AssetId == candidate.SubjectAssetId,
                cancellationToken);
            if (visualEmbedding is null) return Conflict(new { error = "The scene vector for this candidate is no longer available." });
        }
        var decision = new PhotoAnalysisReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"), CandidateId = candidate.Id, Kind = kind,
            ChosenTargetId = chosenTargetId, Note = note, DecidedAtUtc = DateTime.UtcNow
        };
        database.PhotoAnalysisReviewDecisions.Add(decision);
        if (candidate.Kind == PhotoAnalysisCandidateKind.Person
            && candidate.SubjectFaceOccurrenceId is not null
            && kind is PhotoAnalysisDecisionKind.Accepted or PhotoAnalysisDecisionKind.Corrected)
        {
            database.PersonReferenceFaces.Add(new PersonReferenceFace
            {
                PersonId = chosenTargetId!, FaceOccurrenceId = candidate.SubjectFaceOccurrenceId,
                SourceDecisionId = decision.Id, ConfirmedAtUtc = decision.DecidedAtUtc
            });
            await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        }
        if (candidate.Kind == PhotoAnalysisCandidateKind.Location
            && visualEmbedding is not null)
        {
            database.LocationObservations.Add(new LocationObservation
            {
                Id = Guid.NewGuid().ToString("N"), LocationId = chosenTargetId!, AssetId = candidate.SubjectAssetId,
                VisualEmbeddingId = visualEmbedding.Id, SourceDecisionId = decision.Id, ConfirmedAtUtc = decision.DecidedAtUtc
            });
        }
        await SupersedeSiblingCandidates(candidate, decision.DecidedAtUtc, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Created(string.Empty, new PhotoAnalysisReviewDecisionResponse(decision.Id, decision.CandidateId, decision.Kind.ToString(), decision.ChosenTargetId, decision.Note, decision.DecidedAtUtc));
    }

    /// <summary>Records whether a cautious VLM scene observation is useful without changing the model output.</summary>
    [HttpPost("observations/{observationId}/decisions")]
    public async Task<ActionResult> CreateSceneObservationReviewDecision(string observationId, [FromBody] CreateSceneObservationReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        var observation = await database.SceneObservations.SingleOrDefaultAsync(item => item.Id == observationId, cancellationToken);
        if (observation is null) return NotFound();
        if (await database.SceneObservationReviewDecisions.AnyAsync(item => item.ObservationId == observationId, cancellationToken))
            return Conflict(new { error = "This observation has already been reviewed." });
        if (!Enum.TryParse<SceneObservationDecisionKind>(request.Kind, true, out var kind))
            return BadRequest(new { error = "Decision kind must be Confirmed or Rejected." });
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 1000) return BadRequest(new { error = "The review note is limited to 1000 characters." });

        database.SceneObservationReviewDecisions.Add(new SceneObservationReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"), ObservationId = observation.Id, Kind = kind, Note = note, DecidedAtUtc = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Created(string.Empty, null);
    }

    /// <summary>Creates or updates a curated event from a reviewed candidate without changing the cluster evidence.</summary>
    [HttpPost("event-candidates/{candidateId}/decisions")]
    public async Task<ActionResult> CreateEventCandidateReviewDecision(string candidateId, [FromBody] CreateEventCandidateReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        var candidate = await database.EventCandidates.SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null) return NotFound();
        if (candidate.SupersededAtUtc is not null) return Conflict(new { error = "This event candidate has been superseded by a newer run." });
        if (await database.EventCandidateReviewDecisions.AnyAsync(item => item.CandidateId == candidateId, cancellationToken))
            return Conflict(new { error = "This event candidate has already been reviewed." });
        if (!Enum.TryParse<EventCandidateDecisionKind>(request.Kind, true, out var kind))
            return BadRequest(new { error = "Decision kind must be Created, Attached or Rejected." });
        var candidateAssetIds = await database.EventClusterPhotos.Where(photo => photo.ClusterId == candidate.ClusterId).Select(photo => photo.AssetId).ToListAsync(cancellationToken);
        var selectedAssetIds = request.AssetIds?.Distinct().ToList() ?? [];
        if (selectedAssetIds.Any(assetId => !candidateAssetIds.Contains(assetId))) return BadRequest(new { error = "Selected photos must belong to this event candidate." });
        if (kind is EventCandidateDecisionKind.Created or EventCandidateDecisionKind.Attached && selectedAssetIds.Count == 0)
            return BadRequest(new { error = "Choose at least one candidate photo." });
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 1000) return BadRequest(new { error = "The review note is limited to 1000 characters." });

        string? chosenEventId = null;
        if (kind == EventCandidateDecisionKind.Created)
        {
            var title = request.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title)) return BadRequest(new { error = "A new event needs a title." });
            if (request.LocationId is not null && !await database.Locations.AnyAsync(location => location.Id == request.LocationId, cancellationToken)) return BadRequest(new { error = "The selected location no longer exists." });
            var personIds = request.PersonIds?.Distinct().ToList() ?? [];
            if (await database.People.CountAsync(person => personIds.Contains(person.Id), cancellationToken) != personIds.Count) return BadRequest(new { error = "One of the selected people no longer exists." });
            chosenEventId = Guid.NewGuid().ToString("N");
            database.ArchiveEvents.Add(new ArchiveEvent { Id = chosenEventId, Title = title, OccurredOn = request.OccurredOn, LocationId = request.LocationId, CreatedAtUtc = DateTime.UtcNow });
            database.ArchiveEventPeople.AddRange(personIds.Select(personId => new ArchiveEventPerson { EventId = chosenEventId, PersonId = personId }));
            database.ArchiveEventPhotos.AddRange(selectedAssetIds.Select(assetId => new ArchiveEventPhoto { EventId = chosenEventId, AssetId = assetId }));
        }
        else if (kind == EventCandidateDecisionKind.Attached)
        {
            chosenEventId = request.ChosenEventId;
            if (string.IsNullOrWhiteSpace(chosenEventId) || !await database.ArchiveEvents.AnyAsync(@event => @event.Id == chosenEventId, cancellationToken)) return BadRequest(new { error = "Choose an existing event." });
            var existingAssetIds = await database.ArchiveEventPhotos.Where(photo => photo.EventId == chosenEventId).Select(photo => photo.AssetId).ToListAsync(cancellationToken);
            database.ArchiveEventPhotos.AddRange(selectedAssetIds.Except(existingAssetIds).Select(assetId => new ArchiveEventPhoto { EventId = chosenEventId, AssetId = assetId }));
        }
        else if (request.ChosenEventId is not null || selectedAssetIds.Count > 0)
            return BadRequest(new { error = "A rejected candidate cannot change event membership." });

        database.EventCandidateReviewDecisions.Add(new EventCandidateReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"), CandidateId = candidate.Id, Kind = kind, ChosenEventId = chosenEventId,
            SelectedAssetIdsJson = JsonSerializer.Serialize(selectedAssetIds), Note = note, DecidedAtUtc = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Created(string.Empty, null);
    }

    /// <summary>Copies the candidate behind a decision into a fresh unreviewed one, so the same proposal returns
    /// to the review queue. A candidate can only ever carry one decision, so re-deciding needs a new row.</summary>
    // What one review decision covers: a single detected face, or a single photo for a location or event
    // proposal. The lower-ranked alternatives share that subject, so they are the same decision, not a new one.
    private static string ReviewSubjectKey(PhotoAnalysisCandidateKind kind, string subjectAssetId, string? subjectFaceOccurrenceId) =>
        $"{kind}:{subjectFaceOccurrenceId ?? subjectAssetId}";

    // The reviewer answers about a face or a photo, not about each of its five ranked proposals:
    // one decision closes the rest, which stay in the audit trail as superseded. A proposal that
    // already carries its own decision is never touched.
    private async Task SupersedeSiblingCandidates(PhotoAnalysisCandidate decided, DateTime supersededAtUtc, CancellationToken cancellationToken)
    {
        var faceOccurrenceId = decided.SubjectFaceOccurrenceId;
        // The decision settles the face's identity, not one occurrence row of it: a re-detected copy
        // of the same face holds its own open rows, and those are this subject's duplicates rather
        // than new proposals. Without an identity, the occurrence itself stays the subject.
        var identityId = faceOccurrenceId is null
            ? null
            : await database.FaceOccurrences
                .Where(occurrence => occurrence.Id == faceOccurrenceId)
                .Select(occurrence => occurrence.IdentityId)
                .SingleOrDefaultAsync(cancellationToken);
        var siblings = identityId is not null
            ? database.PhotoAnalysisCandidates.Where(item => item.SubjectFaceOccurrenceId != null
                && database.FaceOccurrences.Any(occurrence => occurrence.Id == item.SubjectFaceOccurrenceId && occurrence.IdentityId == identityId))
            : faceOccurrenceId is not null
                ? database.PhotoAnalysisCandidates.Where(item => item.SubjectFaceOccurrenceId == faceOccurrenceId)
                : database.PhotoAnalysisCandidates.Where(item => item.Kind == decided.Kind && item.SubjectAssetId == decided.SubjectAssetId && item.RunId == decided.RunId);

        foreach (var sibling in await siblings
            .Where(item => item.Id != decided.Id && item.SupersededAtUtc == null
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == item.Id))
            .ToListAsync(cancellationToken))
        {
            sibling.SupersededAtUtc = supersededAtUtc;
        }
    }

    private async Task ReopenReviewedCandidate(string decisionId, CancellationToken cancellationToken)
    {
        var decision = await database.PhotoAnalysisReviewDecisions.SingleAsync(item => item.Id == decisionId, cancellationToken);
        var original = await database.PhotoAnalysisCandidates.SingleAsync(item => item.Id == decision.CandidateId, cancellationToken);
        database.PhotoAnalysisCandidates.Add(new PhotoAnalysisCandidate
        {
            Id = Guid.NewGuid().ToString("N"), RunId = original.RunId, Kind = original.Kind,
            SubjectAssetId = original.SubjectAssetId, SubjectFaceOccurrenceId = original.SubjectFaceOccurrenceId,
            ProposedTargetId = original.ProposedTargetId, ProposedLabel = original.ProposedLabel,
            Rank = original.Rank, Score = original.Score, SignalsJson = original.SignalsJson,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<bool> TargetExists(PhotoAnalysisCandidateKind kind, string targetId, CancellationToken cancellationToken) => kind switch
    {
        PhotoAnalysisCandidateKind.Person => await database.People.AnyAsync(item => item.Id == targetId, cancellationToken),
        PhotoAnalysisCandidateKind.Location => await database.Locations.AnyAsync(item => item.Id == targetId, cancellationToken),
        PhotoAnalysisCandidateKind.Event => await database.ArchiveEvents.AnyAsync(item => item.Id == targetId, cancellationToken),
        _ => false
    };

    private static string? CandidateTargetName(PhotoAnalysisCandidate candidate, IEnumerable<Person> people, IEnumerable<Location> locations, IEnumerable<ArchiveEvent> events) => candidate.ProposedTargetId is null ? null : candidate.Kind switch
    {
        PhotoAnalysisCandidateKind.Person => people.SingleOrDefault(item => item.Id == candidate.ProposedTargetId)?.Name,
        PhotoAnalysisCandidateKind.Location => locations.SingleOrDefault(item => item.Id == candidate.ProposedTargetId)?.Name,
        PhotoAnalysisCandidateKind.Event => events.SingleOrDefault(item => item.Id == candidate.ProposedTargetId)?.Title,
        _ => null
    };
}
