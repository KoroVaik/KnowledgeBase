using KnowledgeBase.Api.Controllers.PhotoAnalysis.Contracts;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Core.Pipeline.EventClustering;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KnowledgeBase.Api.Controllers.PhotoAnalysis;

/// <summary>Manually curated people, locations and events for the photo archive.</summary>
[ApiController]
[Route("api/photo-analysis")]
[Authorize]
[Produces("application/json")]
public sealed class PhotoAnalysisController(KnowledgeBaseDbContext database, IChangeNotifier notifier) : ControllerBase
{
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
        var pendingCandidates = (await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.SupersededAtUtc == null && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .OrderBy(candidate => candidate.CreatedAtUtc).ThenBy(candidate => candidate.Rank)
            .ToListAsync(cancellationToken)).Where(candidate => canonicalAssetIds.Contains(candidate.SubjectAssetId)).ToList();
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

        var personCounts = eventPeople.GroupBy(link => link.PersonId).ToDictionary(group => group.Key, group => group.Count());
        var locationCounts = events.Where(@event => @event.LocationId is not null).GroupBy(@event => @event.LocationId!).ToDictionary(group => group.Key, group => group.Count());
        var locationPhotoCounts = (await database.LocationObservations.ToListAsync(cancellationToken))
            .GroupBy(observation => observation.LocationId).ToDictionary(group => group.Key, group => group.Count());
        var locationsById = locations.ToDictionary(location => location.Id);

        return Ok(new PhotoAnalysisResponse(
            people.Select(person => new PersonResponse(person.Id, person.Name, personCounts.GetValueOrDefault(person.Id))).ToList(),
            locations.Select(location => new LocationResponse(location.Id, location.Name, location.Kind.ToString(), locationCounts.GetValueOrDefault(location.Id), locationPhotoCounts.GetValueOrDefault(location.Id))).ToList(),
            events.Select(@event => new ArchiveEventResponse(
                @event.Id, @event.Title, @event.OccurredOn, @event.LocationId,
                @event.LocationId is { } locationId ? locationsById.GetValueOrDefault(locationId)?.Name : null,
                eventPeople.Where(link => link.EventId == @event.Id).Select(link => link.PersonId).ToList(),
                eventPhotos.Where(link => link.EventId == @event.Id).Select(link => link.AssetId).ToList())).ToList(),
            pendingCandidates.Select(candidate => new PhotoAnalysisCandidateResponse(
                candidate.Id,
                candidate.Kind.ToString(),
                candidate.SubjectAssetId,
                candidateAssets.GetValueOrDefault(candidate.SubjectAssetId)?.OriginalFileName ?? "Deleted photo",
                candidate.SubjectFaceOccurrenceId,
                candidate.ProposedTargetId,
                CandidateTargetName(candidate, people, locations, events),
                candidate.ProposedLabel,
                candidate.Rank,
                candidate.Score,
                candidate.SignalsJson,
                candidate.RunId,
                candidate.SubjectFaceOccurrenceId is { } faceOccurrenceId
                    ? faceBoundsByOccurrenceId.GetValueOrDefault(faceOccurrenceId)
                    : null)).ToList(),
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
        return Created(string.Empty, new PersonResponse(person.Id, person.Name, 0));
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
        return Created(string.Empty, new LocationResponse(location.Id, location.Name, location.Kind.ToString(), 0, 0));
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

    /// <summary>Records the user's final review of one model candidate without changing the original model evidence.</summary>
    [HttpPost("candidates/{candidateId}/decisions")]
    public async Task<ActionResult<PhotoAnalysisReviewDecisionResponse>> CreateReviewDecision(string candidateId, [FromBody] CreatePhotoAnalysisReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        var candidate = await database.PhotoAnalysisCandidates.SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null) return NotFound();
        if (await database.PhotoAnalysisReviewDecisions.AnyAsync(item => item.CandidateId == candidateId, cancellationToken))
            return Conflict(new { error = "This candidate has already been reviewed." });
        if (!Enum.TryParse<PhotoAnalysisDecisionKind>(request.Kind, true, out var kind))
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
