using KnowledgeBase.Api.Controllers.PhotoAnalysis.Contracts;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Controllers.PhotoAnalysis;

/// <summary>Face review as rows of people: the worker's latest face grouping, and the decisions that name,
/// remove or ignore the faces in a row.</summary>
[ApiController]
[Route("api/photo-analysis/people-review")]
[Authorize]
[Produces("application/json")]
public sealed class PeopleReviewController(KnowledgeBaseDbContext database, IChangeNotifier notifier) : ControllerBase
{
    private const int ReferenceFacesPerRow = 3;

    /// <summary>Returns the open faces of the latest face grouping: new faces of confirmed people (with their three
    /// most typical confirmed faces), anonymous
    /// groups (largest first), unsorted single faces and ignored groups. Every list is already in display
    /// order, and only faces nobody has decided about yet are included.</summary>
    [HttpGet]
    [ProducesResponseType<PeopleReviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PeopleReviewResponse>> Get(CancellationToken cancellationToken)
    {
        var clusteringPending = await database.ProcessingJobs.AnyAsync(
            job => (job.Kind == JobKind.ClusterFaces || job.Kind == JobKind.RescoreFaces || job.Kind == JobKind.AnalyzeFaces
                    || job.Kind == JobKind.FingerprintAsset || job.Kind == JobKind.MigrateFaceModels)
                && (job.Status == ProcessingStatus.Pending || job.Status == ProcessingStatus.Running),
            cancellationToken);
        var latestRun = await database.FaceClusteringRuns
            .OrderByDescending(run => run.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestRun is null) return Ok(new PeopleReviewResponse(clusteringPending, [], [], [], []));

        var clusters = await database.FaceClusters
            .Where(cluster => cluster.RunId == latestRun.Id)
            .ToDictionaryAsync(cluster => cluster.Id, cancellationToken);
        var clusterIds = clusters.Keys.ToList();
        var candidates = await database.PhotoAnalysisCandidates
            .Where(candidate => candidate.Kind == PhotoAnalysisCandidateKind.Person
                && candidate.SupersededAtUtc == null
                && candidate.SubjectFaceOccurrenceId != null
                && candidate.FaceClusterId != null && clusterIds.Contains(candidate.FaceClusterId)
                && !database.PhotoAnalysisReviewDecisions.Any(decision => decision.CandidateId == candidate.Id))
            .ToListAsync(cancellationToken);
        var occurrenceIds = candidates.Select(candidate => candidate.SubjectFaceOccurrenceId!).ToList();
        var occurrences = await database.FaceOccurrences
            .Where(occurrence => occurrenceIds.Contains(occurrence.Id))
            .ToDictionaryAsync(occurrence => occurrence.Id, cancellationToken);
        // A face confirmed after the grouping ran is settled, even if the worker has not regrouped yet.
        var settledIds = await FaceIdentityState.SettledIdsAsync(database, cancellationToken);
        var openByCluster = candidates
            .Where(candidate => occurrences.TryGetValue(candidate.SubjectFaceOccurrenceId!, out var occurrence)
                && occurrence.IdentityId is not null && !settledIds.Contains(occurrence.IdentityId))
            .GroupBy(candidate => candidate.FaceClusterId!)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Rank)
                .Select(candidate =>
                {
                    var occurrence = occurrences[candidate.SubjectFaceOccurrenceId!];
                    return new PeopleReviewFaceResponse(candidate.Id, occurrence.Id, occurrence.AssetId,
                        new FaceBoundsResponse(occurrence.X, occurrence.Y, occurrence.Width, occurrence.Height), candidate.Score, occurrence.IsPartial);
                })
                .ToList());

        var people = await database.People.ToDictionaryAsync(person => person.Id, cancellationToken);
        PeopleReviewHintResponse? HintFor(FaceCluster cluster) =>
            cluster.HintPersonId is { } personId && cluster.HintScore is { } score && people.TryGetValue(personId, out var person)
                ? new PeopleReviewHintResponse(person.Id, person.Name, score)
                : null;
        List<(FaceCluster Cluster, List<PeopleReviewFaceResponse> Faces)> RowsOf(FaceClusterKind kind) => openByCluster
            .Select(pair => (Cluster: clusters[pair.Key], Faces: pair.Value))
            .Where(row => row.Cluster.Kind == kind && row.Faces.Count > 0)
            .ToList();

        var personRows = RowsOf(FaceClusterKind.Person)
            .Where(row => row.Cluster.PersonId is not null && people.ContainsKey(row.Cluster.PersonId))
            .Select(row => (Person: people[row.Cluster.PersonId!], row.Faces))
            .OrderBy(row => row.Person.Name)
            .ToList();
        var rowPersonIds = personRows.Select(row => row.Person.Id).ToList();
        var references = await (
            from reference in database.PersonReferenceFaces
            join occurrence in database.FaceOccurrences on reference.FaceOccurrenceId equals occurrence.Id
            join asset in database.Assets on occurrence.AssetId equals asset.Id
            where rowPersonIds.Contains(reference.PersonId)
            select new { reference.PersonId, reference.ConfirmedAtUtc, Occurrence = occurrence, asset.OriginalFileName })
            .ToListAsync(cancellationToken);
        // The most typical faces first: highest average similarity to the person's other confirmed faces, so a
        // profile shot or a blurry frame does not stand for the person.
        var referencesByPerson = references
            .GroupBy(reference => reference.PersonId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(reference => group
                    .Where(other => other.Occurrence.Id != reference.Occurrence.Id && other.Occurrence.Embedding.Length == reference.Occurrence.Embedding.Length)
                    .Select(other => FaceEmbeddingMath.CosineSimilarity(reference.Occurrence.Embedding, other.Occurrence.Embedding))
                    .DefaultIfEmpty(0).Average())
                .ThenByDescending(reference => reference.ConfirmedAtUtc)
                .ToList());

        var ignoredGroupCreatedAt = await database.IgnoredFaceGroups
            .ToDictionaryAsync(group => group.Id, group => group.CreatedAtUtc, cancellationToken);

        return Ok(new PeopleReviewResponse(
            clusteringPending,
            personRows.Select(row =>
            {
                var personReferences = referencesByPerson.GetValueOrDefault(row.Person.Id) ?? [];
                return new PeopleReviewPersonRowResponse(
                    row.Person.Id, row.Person.Name,
                    personReferences.Take(ReferenceFacesPerRow).Select(reference => new PersonReferenceFaceResponse(
                        reference.Occurrence.Id, reference.Occurrence.AssetId, reference.OriginalFileName,
                        new FaceBoundsResponse(reference.Occurrence.X, reference.Occurrence.Y, reference.Occurrence.Width, reference.Occurrence.Height),
                        reference.ConfirmedAtUtc)).ToList(),
                    personReferences.Count,
                    row.Faces);
            }).ToList(),
            RowsOf(FaceClusterKind.Anonymous)
                .OrderByDescending(row => row.Faces.Count).ThenBy(row => row.Cluster.Id)
                .Select(row => new PeopleReviewAnonymousRowResponse(row.Cluster.Id, HintFor(row.Cluster), row.Faces)).ToList(),
            RowsOf(FaceClusterKind.Unsorted).SelectMany(row => row.Faces).ToList(),
            RowsOf(FaceClusterKind.Ignored)
                .Where(row => row.Cluster.IgnoredGroupId is not null)
                .OrderBy(row => ignoredGroupCreatedAt.GetValueOrDefault(row.Cluster.IgnoredGroupId!))
                .Select(row => new PeopleReviewIgnoredGroupResponse(row.Cluster.IgnoredGroupId!, HintFor(row.Cluster), row.Faces)).ToList()));
    }

    /// <summary>Names the faces of one row: attaches the checked faces to an existing person (by id, or by a name
    /// that matches one case-insensitively) or creates a new person. The unchecked faces of the same row are removed
    /// in the same save: each is pinned to Unsorted, and a person its row proposed is never suggested for it again.
    /// If any face was decided or regrouped since the list was loaded, nothing is saved and the answer is 409.</summary>
    [HttpPost("submit")]
    [ProducesResponseType<SubmitPeopleReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmitPeopleReviewResponse>> Submit([FromBody] SubmitPeopleReviewRequest request, CancellationToken cancellationToken)
    {
        var (candidates, removed, problem) = await LoadOpenCandidatesAsync(request.CandidateIds, request.RemovedCandidateIds, cancellationToken);
        if (problem is not null) return problem;

        var now = DateTime.UtcNow;
        Person? person;
        var created = false;
        if (!string.IsNullOrWhiteSpace(request.PersonId))
        {
            person = await database.People.SingleOrDefaultAsync(item => item.Id == request.PersonId, cancellationToken);
            if (person is null) return BadRequest(new { error = "The chosen person no longer exists." });
        }
        else
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length == 0) return BadRequest(new { error = "Choose a person or enter a name." });
            if (name.Length > 160) return BadRequest(new { error = "A person's name is limited to 160 characters." });
            person = await database.People.FirstOrDefaultAsync(item => item.Name.ToLower() == name.ToLower(), cancellationToken);
            if (person is null)
            {
                person = new Person { Id = Guid.NewGuid().ToString("N"), Name = name, CreatedAtUtc = now };
                database.People.Add(person);
                created = true;
            }
        }

        var occurrenceIds = candidates.Select(candidate => candidate.SubjectFaceOccurrenceId!).ToList();
        if (await database.PersonReferenceFaces.AnyAsync(reference => occurrenceIds.Contains(reference.FaceOccurrenceId), cancellationToken))
            return Conflict(new { error = "One of these faces is already confirmed. Reload and try again." });
        var partialOccurrenceIds = (await database.FaceOccurrences
            .Where(occurrence => occurrenceIds.Contains(occurrence.Id) && occurrence.IsPartial)
            .Select(occurrence => occurrence.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var candidate in candidates)
        {
            var accepted = candidate.ProposedTargetId == person.Id;
            var decision = new PhotoAnalysisReviewDecision
            {
                Id = Guid.NewGuid().ToString("N"), CandidateId = candidate.Id,
                Kind = accepted ? PhotoAnalysisDecisionKind.Accepted : PhotoAnalysisDecisionKind.Corrected,
                ChosenTargetId = person.Id, DecidedAtUtc = now
            };
            database.PhotoAnalysisReviewDecisions.Add(decision);
            if (!partialOccurrenceIds.Contains(candidate.SubjectFaceOccurrenceId!)) database.PersonReferenceFaces.Add(new PersonReferenceFace
            {
                PersonId = person.Id, FaceOccurrenceId = candidate.SubjectFaceOccurrenceId!,
                SourceDecisionId = decision.Id, ConfirmedAtUtc = now
            });
        }
        AddRemovals(removed, now);
        await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return Ok(new SubmitPeopleReviewResponse(person.Id, person.Name, created));
    }

    /// <summary>Files the checked faces of an anonymous row, or a single unsorted face, into a new ignored group; the
    /// row's unchecked faces are removed to Unsorted in the same save. Ignored faces leave the main review but stay
    /// reviewable, and similar new faces join the group quietly instead of coming back as a new row.</summary>
    [HttpPost("ignore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Ignore([FromBody] IgnorePeopleReviewRequest request, CancellationToken cancellationToken)
    {
        var (candidates, removed, problem) = await LoadOpenCandidatesAsync(request.CandidateIds, request.RemovedCandidateIds, cancellationToken);
        if (problem is not null) return problem;

        var now = DateTime.UtcNow;
        var group = new IgnoredFaceGroup { Id = Guid.NewGuid().ToString("N"), CreatedAtUtc = now };
        database.IgnoredFaceGroups.Add(group);
        database.PhotoAnalysisReviewDecisions.AddRange(candidates.Select(candidate => new PhotoAnalysisReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"), CandidateId = candidate.Id, Kind = PhotoAnalysisDecisionKind.Ignored,
            ChosenTargetId = group.Id, DecidedAtUtc = now
        }));
        AddRemovals(removed, now);
        await ClusterFacesQueue.EnqueueAsync(database, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        notifier.Publish(new ChangeEvent(ChangeResources.PhotoAnalysis, ChangeActions.Updated));
        return NoContent();
    }

    // A Rejected person decision means "not this row", not "close this face": FaceIdentityState reads it as
    // pinned to Unsorted, plus a negative for the person the row proposed.
    private void AddRemovals(IEnumerable<PhotoAnalysisCandidate> removed, DateTime now) =>
        database.PhotoAnalysisReviewDecisions.AddRange(removed.Select(candidate => new PhotoAnalysisReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"), CandidateId = candidate.Id, Kind = PhotoAnalysisDecisionKind.Rejected, DecidedAtUtc = now
        }));

    private async Task<(List<PhotoAnalysisCandidate> Kept, List<PhotoAnalysisCandidate> Removed, ActionResult? Problem)> LoadOpenCandidatesAsync(
        IReadOnlyList<string>? candidateIds, IReadOnlyList<string>? removedCandidateIds, CancellationToken cancellationToken)
    {
        static List<string> Clean(IReadOnlyList<string>? ids) => ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];
        var keptIds = Clean(candidateIds);
        var removedIds = Clean(removedCandidateIds);
        if (keptIds.Count == 0) return ([], [], BadRequest(new { error = "Choose at least one face." }));
        if (keptIds.Intersect(removedIds).Any()) return ([], [], BadRequest(new { error = "A face cannot be both kept and removed." }));

        var ids = keptIds.Concat(removedIds).ToList();
        var candidates = await database.PhotoAnalysisCandidates
            .Where(candidate => ids.Contains(candidate.Id) && candidate.Kind == PhotoAnalysisCandidateKind.Person)
            .ToListAsync(cancellationToken);
        var decided = await database.PhotoAnalysisReviewDecisions
            .AnyAsync(decision => ids.Contains(decision.CandidateId), cancellationToken);
        if (candidates.Count != ids.Count || decided
            || candidates.Any(candidate => candidate.SupersededAtUtc is not null || candidate.SubjectFaceOccurrenceId is null))
            return ([], [], Conflict(new { error = "Some of these faces changed since the list was loaded. Reload and try again." }));
        var removedSet = removedIds.ToHashSet();
        return (candidates.Where(candidate => !removedSet.Contains(candidate.Id)).ToList(),
            candidates.Where(candidate => removedSet.Contains(candidate.Id)).ToList(), null);
    }
}
