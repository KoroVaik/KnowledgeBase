using KnowledgeBase.Core.FaceAnalysis;
using KnowledgeBase.Core.Persistence;
using KnowledgeBase.Core.Pipeline;
using KnowledgeBase.Worker.FaceAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeBase.Tests;

public sealed class FaceModelMigrationTests
{
    [Fact]
    public async Task AlignmentChangeQueuesOneMigrationAndStopsWhenEmbeddingsAreCurrent()
    {
        await using var database = CreateDatabase();
        var occurrence = await SeedFaceAsync(database, "insightface w600k_r50 (ArcFace)");
        var analyzer = new CurrentFaceAnalyzer();

        Assert.True(await FaceModelMigrationQueue.EnqueueIfGapAsync(database, analyzer, CancellationToken.None));
        var job = Assert.Single(await database.ProcessingJobs.ToListAsync());
        Assert.Equal(JobKind.MigrateFaceModels, job.Kind);
        Assert.Equal(ProcessingStatus.Pending, job.Status);
        Assert.False(await FaceModelMigrationQueue.EnqueueIfGapAsync(database, analyzer, CancellationToken.None));

        job.Status = ProcessingStatus.Running;
        await database.SaveChangesAsync();
        Assert.False(await FaceModelMigrationQueue.EnqueueIfGapAsync(database, analyzer, CancellationToken.None));

        occurrence.EmbeddingModelKey = analyzer.EmbeddingModelKey;
        job.Status = ProcessingStatus.Done;
        await database.SaveChangesAsync();
        Assert.False(await FaceModelMigrationQueue.EnqueueIfGapAsync(database, analyzer, CancellationToken.None));
        Assert.Single(await database.ProcessingJobs.ToListAsync());
    }

    [Theory]
    [InlineData(ProcessingStatus.Pending, false)]
    [InlineData(ProcessingStatus.Running, false)]
    [InlineData(ProcessingStatus.Done, true)]
    public async Task ClusteringWaitsUntilEmbeddingMigrationFinishes(ProcessingStatus migrationStatus, bool shouldCluster)
    {
        await using var database = CreateDatabase();
        await SeedFaceAsync(database, FaceOnnxFaceAnalyzer.CurrentEmbeddingModelKey);
        database.ProcessingJobs.Add(new ProcessingJob
        {
            Id = "migration", Kind = JobKind.MigrateFaceModels, Status = migrationStatus, CreatedAtUtc = DateTime.UtcNow,
        });
        var existingCandidate = new PhotoAnalysisCandidate
        {
            Id = "old-candidate", RunId = "detection", Kind = PhotoAnalysisCandidateKind.Person,
            SubjectAssetId = "photo", SubjectFaceOccurrenceId = "face", Rank = 1, Score = 0,
            SignalsJson = "{}", CreatedAtUtc = DateTime.UtcNow,
        };
        database.PhotoAnalysisCandidates.Add(existingCandidate);
        await database.SaveChangesAsync();
        var handler = new ClusterFacesHandler(database, Options.Create(new FaceClusteringOptions()));
        var job = new ProcessingJob
        {
            Id = "grouping", Kind = JobKind.ClusterFaces, Status = ProcessingStatus.Running, CreatedAtUtc = DateTime.UtcNow,
        };

        await handler.HandleAsync(job, CancellationToken.None);
        await database.SaveChangesAsync();

        Assert.Equal(shouldCluster ? 1 : 0, await database.FaceClusteringRuns.CountAsync());
        Assert.Equal(shouldCluster, existingCandidate.SupersededAtUtc.HasValue);
        Assert.Equal(shouldCluster ? 2 : 1, await database.PhotoAnalysisCandidates.CountAsync());
    }

    private static KnowledgeBaseDbContext CreateDatabase() => new(
        new DbContextOptionsBuilder<KnowledgeBaseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<FaceOccurrence> SeedFaceAsync(KnowledgeBaseDbContext database, string embeddingModelKey)
    {
        var now = DateTime.UtcNow;
        database.Assets.Add(new AssetRecord
        {
            Id = "photo", StoredFileName = "photo.jpg", OriginalFileName = "photo.jpg", ContentType = "image/jpeg",
            SizeBytes = 1, UploadedAtUtc = now,
        });
        database.PhotoAnalysisRuns.Add(new PhotoAnalysisRun
        {
            Id = "detection", AssetId = "photo", PipelineVersion = FaceAnalysisPipeline.CurrentDetectionVersion,
            ModelKey = "detector", ConfigurationHash = "test", CompletedAtUtc = now,
        });
        database.FaceIdentities.Add(new FaceIdentity { Id = "identity", AssetId = "photo", CreatedAtUtc = now });
        var occurrence = new FaceOccurrence
        {
            Id = "face", RunId = "detection", AssetId = "photo", IdentityId = "identity", X = 10, Y = 10,
            Width = 100, Height = 100, DetectionScore = 0.9, IsPartial = false, NeedsReview = false,
            LandmarksJson = "[]", Embedding = [1, 0], EmbeddingModelKey = embeddingModelKey, CreatedAtUtc = now,
        };
        database.FaceOccurrences.Add(occurrence);
        await database.SaveChangesAsync();
        return occurrence;
    }

    private sealed class CurrentFaceAnalyzer : IFaceAnalyzer
    {
        public string ModelKey => EmbeddingModelKey;
        public string EmbeddingModelKey => FaceOnnxFaceAnalyzer.CurrentEmbeddingModelKey;
        public string ConfigurationHash => "test";

        public Task<IReadOnlyList<DetectedFace>> AnalyzeAsync(byte[] imageBytes, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Migration scheduling must not run face detection.");
    }
}
