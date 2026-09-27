using KnowledgeBase.Api.Controllers.PhotoAnalysis;
using KnowledgeBase.Api.Controllers.PhotoAnalysis.Contracts;
using KnowledgeBase.Core.Ai;
using KnowledgeBase.Core.Ai.Configuration;
using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline.FaceAnalysis;
using KnowledgeBase.Core.RealTime;
using KnowledgeBase.Core.Storage;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class FaceValidationTests
{
    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData(FaceValidationSubject.AnimalFace, false, false, false)]
    [InlineData(FaceValidationSubject.StatueOrArtwork, false, false, false)]
    [InlineData(FaceValidationSubject.NotFace, false, false, false)]
    [InlineData(FaceValidationSubject.Uncertain, false, false, false)]
    [InlineData(FaceValidationSubject.HumanFace, true, false, true)]
    [InlineData(FaceValidationSubject.HumanFace, true, true, false)]
    [InlineData(FaceValidationSubject.HumanFace, false, true, false)]
    [InlineData(FaceValidationSubject.HumanFace, false, false, true)]
    public void HumanVerdictRequiresDetectorConfirmationButPhotoEdgeIsAdvisory(FaceValidationSubject? subject, bool partial, bool detectorReview, bool expected)
    {
        var face = Face("face", partial: partial, needsReview: detectorReview);
        var validation = subject is null ? null : Validation(face, subject.Value);
        Assert.Equal(expected, FaceValidationPolicy.Assess(face, validation, null).CanUseForPeople);
    }

    [Fact]
    public void CpuWarningsCoexistAndStayAdvisoryWhileManualDecisionsOverrideTheModel()
    {
        var face = Face("face");
        var validation = Validation(face);
        validation.MinSidePixels = 12;
        validation.Sharpness112 = 2;
        validation.TouchesImageEdge = true;
        var assessment = FaceValidationPolicy.Assess(face, validation, null);
        Assert.True(assessment.CanUseForPeople);
        Assert.Contains("SmallCrop", assessment.Reasons);
        Assert.Contains("LowSharpness", assessment.Reasons);
        Assert.Contains("ImageEdge", assessment.Reasons);
        validation.Subject = FaceValidationSubject.NotFace;
        Assert.True(FaceValidationPolicy.Assess(face, validation, FaceValidationDecisionKind.Approved).CanUseForPeople);
        validation.Subject = FaceValidationSubject.HumanFace;
        Assert.False(FaceValidationPolicy.Assess(face, validation, FaceValidationDecisionKind.Excluded).CanUseForPeople);
        validation.PipelineVersion = "old";
        Assert.Equal("Pending", FaceValidationPolicy.Assess(face, validation, null).Status);
    }

    [Fact]
    public async Task InterruptedAssetResumesWithoutRepeatingCompletedModelCalls()
    {
        await using var db = Database();
        await Seed(db, "first");
        await Seed(db, "second");
        var analyzer = new Analyzer { FailOnCall = 2 };
        var reader = new Reader();
        var handler = new FaceValidationHandler(db, reader, analyzer, Options.Create(new OllamaOptions()));
        var job = ProcessingJob.Queue("photo", JobKind.ValidateFaces);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job, CancellationToken.None));
        Assert.Equal(1, await db.FaceValidations.CountAsync(item => item.CompletedAtUtc != null));
        var pending = await db.FaceValidations.SingleAsync(item => item.CompletedAtUtc == null);
        Assert.Null(pending.Subject);
        Assert.NotNull(pending.LastError);
        Assert.NotNull(pending.MinSidePixels);
        analyzer.FailOnCall = null;
        await handler.HandleAsync(job, CancellationToken.None);
        Assert.Equal(3, analyzer.Calls);
        Assert.Equal(2, await db.FaceValidations.CountAsync(item => item.CompletedAtUtc != null));
        Assert.All(await db.FaceValidations.ToListAsync(), item => Assert.Null(item.LastError));
        var reads = reader.Reads;
        await handler.HandleAsync(job, CancellationToken.None);
        Assert.Equal(3, analyzer.Calls);
        Assert.Equal(reads, reader.Reads);
    }

    [Fact]
    public async Task MalformedSubjectStaysPendingInsteadOfBecomingEligible()
    {
        await using var db = Database();
        var face = await Seed(db, "face");
        var analyzer = new Analyzer { Subject = "definitely_human" };
        var handler = new FaceValidationHandler(db, new Reader(), analyzer, Options.Create(new OllamaOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(ProcessingJob.Queue("photo", JobKind.ValidateFaces), CancellationToken.None));
        var validation = await db.FaceValidations.SingleAsync();
        Assert.Equal("Pending", FaceValidationPolicy.Assess(face, validation, null).Status);
        Assert.NotNull(validation.LastError);
    }

    [Fact]
    public async Task BackfillSkipsReferencesAndManualDecisionsAndResetsOnlyStaleCache()
    {
        await using var db = Database();
        var confirmed = await Seed(db, "confirmed");
        await AddReference(db, confirmed);
        var manuallyReviewed = await Seed(db, "manual");
        db.FaceValidationReviewDecisions.Add(Decision(manuallyReviewed, FaceValidationDecisionKind.Excluded));
        var current = await Seed(db, "cached");
        db.FaceValidations.Add(Validation(current));
        await db.SaveChangesAsync();
        Assert.Equal(0, await FaceValidationBackfill.EnqueueAsync(db, "qwen2.5vl:7b", CancellationToken.None));
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
        Assert.Equal(1, await FaceValidationBackfill.EnqueueAsync(db, "new-model", CancellationToken.None));
        Assert.Null((await db.FaceValidations.SingleAsync()).CompletedAtUtc);
        Assert.Null((await db.FaceValidations.SingleAsync()).Evidence);
        Assert.Single(await db.ProcessingJobs.Where(job => job.Kind == JobKind.ValidateFaces).ToListAsync());
        await FaceValidationBackfill.EnqueueAsync(db, "new-model", CancellationToken.None);
        Assert.Single(await db.ProcessingJobs.Where(job => job.Kind == JobKind.ValidateFaces).ToListAsync());
        Assert.Single(await db.PersonReferenceFaces.ToListAsync());
        Assert.Single(await db.FaceValidationReviewDecisions.ToListAsync());
    }

    [Fact]
    public async Task RetryReusesTheAssetJobAndPreservesCompletedFaces()
    {
        await using var db = Database();
        var face = await Seed(db, "face");
        db.FaceValidations.Add(Validation(face));
        var job = ProcessingJob.Queue("photo", JobKind.ValidateFaces);
        job.Status = ProcessingStatus.Failed;
        job.Attempts = 3;
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        Assert.True(await FaceValidationQueue.EnqueueAsync(db, "photo", CancellationToken.None));
        Assert.False(await FaceValidationQueue.EnqueueAsync(db, "photo", CancellationToken.None));
        await db.SaveChangesAsync();
        Assert.Single(await db.ProcessingJobs.ToListAsync());
        Assert.Equal(ProcessingStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.NotNull((await db.FaceValidations.SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task HttpTimeoutRemainsRetriableAndDoesNotLookLikeWorkerShutdown()
    {
        await using var db = Database();
        await Seed(db, "face");
        var handler = new FaceValidationHandler(db, new Reader(), new Analyzer { Timeout = true }, Options.Create(new OllamaOptions()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(ProcessingJob.Queue("photo", JobKind.ValidateFaces), CancellationToken.None));
        Assert.IsType<TaskCanceledException>(error.InnerException);
        Assert.Null((await db.FaceValidations.SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task IgnoredGroupsRemainIgnoredEvenWithoutValidation()
    {
        await using var db = Database();
        var face = await Seed(db, "ignored");
        var candidate = await AddCandidate(db, face);
        db.IgnoredFaceGroups.Add(new IgnoredFaceGroup { Id = "ignored-group", CreatedAtUtc = DateTime.UtcNow });
        db.PhotoAnalysisReviewDecisions.Add(new PhotoAnalysisReviewDecision { Id = "ignore-decision", CandidateId = candidate.Id, Kind = PhotoAnalysisDecisionKind.Ignored,
            ChosenTargetId = "ignored-group", DecidedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(0, await FaceValidationBackfill.EnqueueAsync(db, "qwen2.5vl:7b", CancellationToken.None));
        var handler = new ClusterFacesHandler(db, Options.Create(new FaceClusteringOptions()));
        await handler.HandleAsync(ProcessingJob.Queue("photo", JobKind.ClusterFaces), CancellationToken.None);
        await db.SaveChangesAsync();
        var latest = await db.FaceClusteringRuns.OrderByDescending(run => run.CompletedAtUtc).FirstAsync();
        var cluster = Assert.Single(await db.FaceClusters.Where(item => item.RunId == latest.Id).ToListAsync());
        Assert.Equal(FaceClusterKind.Ignored, cluster.Kind);
        Assert.Equal("ignored-group", cluster.IgnoredGroupId);
    }

    [Fact]
    public async Task PendingFaceCannotJoinKnownPersonButValidatedEdgeFaceCanUseEdgeReference()
    {
        await using var db = Database();
        var reference = await Seed(db, "reference", partial: true);
        await AddReference(db, reference);
        var human = await Seed(db, "human", partial: true);
        var pending = await Seed(db, "pending");
        var validation = Validation(human);
        validation.TouchesImageEdge = true;
        db.FaceValidations.Add(validation);
        await db.SaveChangesAsync();
        var handler = new ClusterFacesHandler(db, Options.Create(new FaceClusteringOptions()));
        await handler.HandleAsync(ProcessingJob.Queue("photo", JobKind.ClusterFaces), CancellationToken.None);
        await db.SaveChangesAsync();
        var placements = await (from candidate in db.PhotoAnalysisCandidates join cluster in db.FaceClusters on candidate.FaceClusterId equals cluster.Id
            select new { candidate.SubjectFaceOccurrenceId, cluster.Kind }).ToListAsync();
        Assert.Contains(placements, item => item.SubjectFaceOccurrenceId == human.Id && item.Kind == FaceClusterKind.Person);
        Assert.Contains(placements, item => item.SubjectFaceOccurrenceId == pending.Id && item.Kind == FaceClusterKind.Unsorted);
        Assert.Single(await db.PersonReferenceFaces.ToListAsync());
    }

    [Fact]
    public async Task ApiHidesStaleAutomaticGroupsButPersonAssignmentAlsoApprovesPendingFace()
    {
        await using var db = Database();
        var face = await Seed(db, "face", partial: true);
        var candidate = await AddCandidate(db, face);
        var api = new PeopleReviewController(db, new Notifier());
        var response = Assert.IsType<PeopleReviewResponse>(Assert.IsType<OkObjectResult>((await api.Get(CancellationToken.None)).Result).Value);
        Assert.Empty(response.AnonymousRows);
        Assert.Equal("Pending", Assert.Single(response.Unsorted).Validation.Status);
        var request = new SubmitPeopleReviewRequest([candidate.Id], [], null, "Test person");
        Assert.IsType<OkObjectResult>((await api.Submit(request, CancellationToken.None)).Result);
        Assert.Single(await db.PersonReferenceFaces.ToListAsync());
        Assert.Equal(FaceValidationDecisionKind.Approved, Assert.Single(await db.FaceValidationReviewDecisions.ToListAsync()).Kind);
        Assert.True((await FaceValidationPolicy.LoadAsync(db, CancellationToken.None)).Assess(face).CanUseForPeople);
        Assert.True(face.IsPartial);
        Assert.IsType<ConflictObjectResult>(await api.ReviewValidation(face.Id, new("Excluded"), CancellationToken.None));
        Assert.Single(await db.FaceValidationReviewDecisions.ToListAsync());
    }

    [Theory]
    [InlineData(FaceClusterKind.Unsorted, false)]
    [InlineData(FaceClusterKind.Unsorted, true)]
    [InlineData(FaceClusterKind.Ignored, false)]
    [InlineData(FaceClusterKind.Ignored, true)]
    public async Task NamingApprovesOnlySelectedBlockedFacesAndPreservesModelEvidence(FaceClusterKind groupKind, bool existingPerson)
    {
        await using var db = Database();
        var flagged = await Seed(db, "flagged");
        var excluded = await Seed(db, "excluded");
        var eligible = await Seed(db, "eligible");
        var removed = await Seed(db, "removed");
        var faces = new[] { flagged, excluded, eligible, removed };
        var candidates = new List<PhotoAnalysisCandidate>();
        foreach (var face in faces) candidates.Add(await AddCandidate(db, face, groupKind));
        db.FaceValidations.Add(Validation(flagged, FaceValidationSubject.NotFace));
        db.FaceValidations.Add(Validation(eligible));
        db.FaceValidationReviewDecisions.Add(Decision(excluded, FaceValidationDecisionKind.Excluded));
        if (existingPerson) db.People.Add(new Person { Id = "chosen", Name = "Chosen person", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var api = new PeopleReviewController(db, new Notifier());
        var request = new SubmitPeopleReviewRequest(candidates.Take(3).Select(candidate => candidate.Id).ToList(),
            [candidates[3].Id], existingPerson ? "chosen" : null, existingPerson ? null : "Chosen person");

        var result = Assert.IsType<SubmitPeopleReviewResponse>(Assert.IsType<OkObjectResult>((await api.Submit(request, CancellationToken.None)).Result).Value);

        Assert.Equal(!existingPerson, result.Created);
        var references = await db.PersonReferenceFaces.ToListAsync();
        Assert.Equal(3, references.Count);
        Assert.All(references, reference => Assert.Equal(result.PersonId, reference.PersonId));
        Assert.DoesNotContain(references, reference => reference.FaceOccurrenceId == removed.Id);
        var approvals = await db.FaceValidationReviewDecisions.Where(decision => decision.Kind == FaceValidationDecisionKind.Approved).ToListAsync();
        Assert.Equal(2, approvals.Count);
        Assert.Contains(approvals, decision => decision.FaceOccurrenceId == flagged.Id);
        Assert.Contains(approvals, decision => decision.FaceOccurrenceId == excluded.Id);
        var state = await FaceValidationPolicy.LoadAsync(db, CancellationToken.None);
        Assert.True(state.Assess(excluded).CanUseForPeople);
        Assert.False(state.Assess(removed).CanUseForPeople);
        Assert.Equal(FaceValidationSubject.NotFace, (await db.FaceValidations.SingleAsync(validation => validation.FaceOccurrenceId == flagged.Id)).Subject);
        Assert.Contains(await db.PhotoAnalysisReviewDecisions.ToListAsync(), decision => decision.CandidateId == candidates[3].Id && decision.Kind == PhotoAnalysisDecisionKind.Rejected);
        Assert.Single(await db.ProcessingJobs.Where(job => job.Kind == JobKind.ClusterFaces).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedAssignmentDoesNotApproveFacesOrSavePartialDecisions(bool staleCandidate)
    {
        await using var db = Database();
        var face = await Seed(db, "pending");
        var candidate = await AddCandidate(db, face);
        if (staleCandidate)
        {
            candidate.SupersededAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var api = new PeopleReviewController(db, new Notifier());
        var result = (await api.Submit(new([candidate.Id], [], null, staleCandidate ? "Test person" : ""), CancellationToken.None)).Result;
        if (staleCandidate) Assert.IsType<ConflictObjectResult>(result);
        else Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.FaceValidationReviewDecisions.ToListAsync());
        Assert.Empty(await db.PersonReferenceFaces.ToListAsync());
        Assert.Empty(await db.PhotoAnalysisReviewDecisions.ToListAsync());
        Assert.Empty(await db.People.ToListAsync());
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
    }

    [Fact]
    public async Task ValidatedEdgeFaceCanBeAssignedWithoutManualValidityOverride()
    {
        await using var db = Database();
        var face = await Seed(db, "face", partial: true);
        var candidate = await AddCandidate(db, face);
        var validation = Validation(face);
        validation.TouchesImageEdge = true;
        db.FaceValidations.Add(validation);
        await db.SaveChangesAsync();
        var api = new PeopleReviewController(db, new Notifier());
        var response = Assert.IsType<PeopleReviewResponse>(Assert.IsType<OkObjectResult>((await api.Get(CancellationToken.None)).Result).Value);
        var visibleFace = Assert.Single(Assert.Single(response.AnonymousRows).Faces);
        Assert.True(visibleFace.Validation.CanUseForPeople);
        Assert.Contains("ImageEdge", visibleFace.Validation.Reasons);
        Assert.IsType<OkObjectResult>((await api.Submit(new([candidate.Id], [], null, "Test person"), CancellationToken.None)).Result);
        Assert.Single(await db.PersonReferenceFaces.ToListAsync());
        Assert.Empty(await db.FaceValidationReviewDecisions.ToListAsync());
    }

    [Fact]
    public async Task UnsortedOrderStaysStableAfterOneApprovalAndRegrouping()
    {
        await using var db = Database();
        var detectedAt = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var ids = new[] { "z-oldest", "a-review", "b-review", "c-review", "d-review", "e-review",
            "f-review", "g-review", "h-review", "i-review", "j-review", "m-ready" };
        foreach (var id in ids.Reverse())
        {
            var face = await Seed(db, id);
            db.Entry(face).Property(item => item.CreatedAtUtc).CurrentValue = id == "z-oldest"
                ? detectedAt.AddMinutes(-1) : id == "m-ready" ? detectedAt.AddMinutes(1) : detectedAt;
            if (id == "a-review") face.Embedding = [0, 1];
            db.FaceValidations.Add(Validation(face, id == "m-ready" ? FaceValidationSubject.HumanFace : FaceValidationSubject.NotFace));
            if (id == "z-oldest") db.FaceValidationReviewDecisions.Add(Decision(face, FaceValidationDecisionKind.Excluded));
        }
        await db.SaveChangesAsync();
        var handler = new ClusterFacesHandler(db, Options.Create(new FaceClusteringOptions()));
        await handler.HandleAsync(ProcessingJob.Queue("photo", JobKind.ClusterFaces), CancellationToken.None);
        await db.SaveChangesAsync();
        var api = new PeopleReviewController(db, new Notifier());
        var before = Assert.IsType<PeopleReviewResponse>(Assert.IsType<OkObjectResult>((await api.Get(CancellationToken.None)).Result).Value);
        Assert.Equal(ids, before.Unsorted.Select(face => face.FaceOccurrenceId));

        Assert.IsType<NoContentResult>(await api.ReviewValidation("a-review", new("Approved"), CancellationToken.None));
        await handler.HandleAsync(ProcessingJob.Queue("photo", JobKind.ClusterFaces), CancellationToken.None);
        await db.SaveChangesAsync();
        var after = Assert.IsType<PeopleReviewResponse>(Assert.IsType<OkObjectResult>((await api.Get(CancellationToken.None)).Result).Value);

        Assert.Equal(ids, after.Unsorted.Select(face => face.FaceOccurrenceId));
        Assert.Equal(before.Unsorted.Take(10).Select(face => face.FaceOccurrenceId), after.Unsorted.Take(10).Select(face => face.FaceOccurrenceId));
        Assert.Equal("a-review", Assert.Single(after.Unsorted, face => face.Validation.Status == "Approved").FaceOccurrenceId);
        Assert.All(after.Unsorted.Where(face => face.FaceOccurrenceId != "a-review"), face =>
            Assert.Equal(before.Unsorted.Single(previous => previous.FaceOccurrenceId == face.FaceOccurrenceId).Validation.Status, face.Validation.Status));
        Assert.DoesNotContain(after.Unsorted, face => before.Unsorted.Any(previous => previous.CandidateId == face.CandidateId));
    }

    [Fact]
    public void OriginalSizeAndEdgeAreMeasuredBeforeResizingTheModelInput()
    {
        using var image = new Image<Rgb24>(100, 100, new Rgb24(90, 90, 90));
        var face = new FaceOccurrence { Id = "face", RunId = "run", AssetId = "photo", X = 0, Y = 0, Width = 10, Height = 20,
            DetectionScore = 1, IsPartial = true, NeedsReview = false, LandmarksJson = "[]", Embedding = [1, 0], CreatedAtUtc = DateTime.UtcNow };
        var validation = Validation(face);
        FaceValidationImages.Measure(image, face, validation);
        Assert.Equal(10, validation.MinSidePixels);
        Assert.Equal(0, validation.Sharpness112);
        Assert.True(validation.TouchesImageEdge);
        var pair = FaceValidationImages.Prepare(image, face);
        using var crop = Image.Load<Rgb24>(pair.Crop.Bytes);
        using var context = Image.Load<Rgb24>(pair.Context.Bytes);
        Assert.Equal(256, Math.Max(crop.Width, crop.Height));
        Assert.Equal(448, Math.Max(context.Width, context.Height));
        Assert.Equal(new Rgb24(255, 220, 0), context[0, 0]);
    }

    private static KnowledgeBaseDbContext Database() => new(new DbContextOptionsBuilder<KnowledgeBaseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static FaceOccurrence Face(string id, bool partial = false, bool needsReview = false) => new()
    {
        Id = id, RunId = "run", AssetId = "photo", IdentityId = "identity-" + id, X = 10, Y = 10, Width = 30, Height = 40,
        DetectionScore = .9, IsPartial = partial, NeedsReview = needsReview, LandmarksJson = "[]", Embedding = [1, 0], CreatedAtUtc = DateTime.UtcNow
    };
    private static async Task<FaceOccurrence> Seed(KnowledgeBaseDbContext db, string id, bool partial = false)
    {
        if (!await db.Assets.AnyAsync())
        {
            db.Assets.Add(new AssetRecord { Id = "photo", StoredFileName = "photo.png", OriginalFileName = "photo.png", ContentType = "image/png", SizeBytes = 100, UploadedAtUtc = DateTime.UtcNow, ContentSha256 = "hash" });
            db.PhotoAnalysisRuns.Add(new PhotoAnalysisRun { Id = "run", AssetId = "photo", PipelineVersion = FaceAnalysisPipeline.CurrentDetectionVersion, ModelKey = "detector", ConfigurationHash = "test", CompletedAtUtc = DateTime.UtcNow });
        }
        var face = Face(id, partial);
        db.FaceIdentities.Add(new FaceIdentity { Id = face.IdentityId!, AssetId = face.AssetId, CreatedAtUtc = DateTime.UtcNow });
        db.FaceOccurrences.Add(face);
        await db.SaveChangesAsync();
        return face;
    }
    private static FaceValidation Validation(FaceOccurrence face, FaceValidationSubject subject = FaceValidationSubject.HumanFace) => new()
    {
        FaceOccurrenceId = face.Id, PipelineVersion = FaceValidationPolicy.PipelineVersion, ConfigurationHash = FaceValidationPrompt.ConfigurationHash("qwen2.5vl:7b"),
        InputHash = FaceValidationImages.InputHash(face, "hash"), ModelKey = "qwen2.5vl:7b", Subject = subject, Evidence = "Visible evidence.", CompletedAtUtc = DateTime.UtcNow
    };
    private static FaceValidationReviewDecision Decision(FaceOccurrence face, FaceValidationDecisionKind kind) => new()
    {
        Id = Guid.NewGuid().ToString("N"), FaceIdentityId = face.IdentityId!, FaceOccurrenceId = face.Id, Kind = kind, DecidedAtUtc = DateTime.UtcNow
    };
    private static async Task<PhotoAnalysisCandidate> AddCandidate(KnowledgeBaseDbContext db, FaceOccurrence face, FaceClusterKind kind = FaceClusterKind.Anonymous)
    {
        var run = new FaceClusteringRun { Id = "grouping-" + face.Id, PipelineVersion = "test", ConfigurationHash = "test", CompletedAtUtc = DateTime.UtcNow };
        db.FaceClusteringRuns.Add(run);
        if (kind == FaceClusterKind.Ignored && !await db.IgnoredFaceGroups.AnyAsync(group => group.Id == "test-ignored-group"))
            db.IgnoredFaceGroups.Add(new IgnoredFaceGroup { Id = "test-ignored-group", CreatedAtUtc = DateTime.UtcNow });
        db.FaceClusters.Add(new FaceCluster { Id = "cluster-" + face.Id, RunId = run.Id, Kind = kind,
            IgnoredGroupId = kind == FaceClusterKind.Ignored ? "test-ignored-group" : null, CreatedAtUtc = DateTime.UtcNow });
        var candidate = new PhotoAnalysisCandidate { Id = "candidate-" + face.Id, RunId = "run", Kind = PhotoAnalysisCandidateKind.Person, SubjectAssetId = "photo", SubjectFaceOccurrenceId = face.Id,
            FaceClusterId = "cluster-" + face.Id, Rank = 1, Score = 1, SignalsJson = "{}", CreatedAtUtc = DateTime.UtcNow };
        db.PhotoAnalysisCandidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate;
    }
    private static async Task AddReference(KnowledgeBaseDbContext db, FaceOccurrence face)
    {
        var candidate = await AddCandidate(db, face);
        db.People.Add(new Person { Id = "person", Name = "Known", CreatedAtUtc = DateTime.UtcNow });
        db.PhotoAnalysisReviewDecisions.Add(new PhotoAnalysisReviewDecision { Id = "reference-decision", CandidateId = candidate.Id, Kind = PhotoAnalysisDecisionKind.Accepted, ChosenTargetId = "person", DecidedAtUtc = DateTime.UtcNow });
        db.PersonReferenceFaces.Add(new PersonReferenceFace { PersonId = "person", FaceOccurrenceId = face.Id, SourceDecisionId = "reference-decision", ConfirmedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    private sealed class Reader : IAssetContentReader
    {
        public int Reads { get; private set; }
        public Task<byte[]> ReadBytesAsync(string storedFileName, CancellationToken cancellationToken)
        {
            Reads++;
            using var image = new Image<Rgb24>(80, 80, new Rgb24(100, 90, 80));
            using var bytes = new MemoryStream();
            image.SaveAsPng(bytes);
            return Task.FromResult(bytes.ToArray());
        }
    }
    private sealed class Analyzer : IContentAnalyzer
    {
        public int Calls { get; private set; }
        public int? FailOnCall { get; set; }
        public string Subject { get; set; } = "human_face";
        public bool Timeout { get; set; }
        public Task EnsureModelAvailableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<T> RunAsync<T>(AiTask task, CancellationToken cancellationToken) where T : class
        {
            Calls++;
            Assert.NotNull(task.Image);
            Assert.Single(task.AdditionalImages!);
            Assert.Equal(0, task.GenerationOptions!.Temperature);
            if (Timeout) throw new TaskCanceledException("Simulated HTTP timeout.");
            if (Calls == FailOnCall) throw new InvalidOperationException("Simulated inference failure.");
            return Task.FromResult((T)(object)new FaceValidationAnswer(Subject, "Visible facial features."));
        }
    }
    private sealed class Notifier : IChangeNotifier
    {
        public void Publish(ChangeEvent change) { }
        public ChangeSubscription Subscribe() => throw new NotSupportedException();
    }
}
