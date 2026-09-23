using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Core.Persistence;

public sealed class KnowledgeBaseDbContext(DbContextOptions<KnowledgeBaseDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AssetRecord> Assets => Set<AssetRecord>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteLink> NoteLinks => Set<NoteLink>();

    public DbSet<SynthesisSource> SynthesisSources => Set<SynthesisSource>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

    public DbSet<TagParent> TagParents => Set<TagParent>();

    public DbSet<TagParentSuggestion> TagParentSuggestions => Set<TagParentSuggestion>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<ArchiveEvent> ArchiveEvents => Set<ArchiveEvent>();

    public DbSet<ArchiveEventPhoto> ArchiveEventPhotos => Set<ArchiveEventPhoto>();

    public DbSet<ArchiveEventPerson> ArchiveEventPeople => Set<ArchiveEventPerson>();

    public DbSet<PhotoAnalysisRun> PhotoAnalysisRuns => Set<PhotoAnalysisRun>();

    public DbSet<PhotoAnalysisCandidate> PhotoAnalysisCandidates => Set<PhotoAnalysisCandidate>();

    public DbSet<PhotoAnalysisReviewDecision> PhotoAnalysisReviewDecisions => Set<PhotoAnalysisReviewDecision>();

    public DbSet<FaceOccurrence> FaceOccurrences => Set<FaceOccurrence>();

    public DbSet<FaceComparisonRun> FaceComparisonRuns => Set<FaceComparisonRun>();
    public DbSet<FaceComparisonResult> FaceComparisonResults => Set<FaceComparisonResult>();
    public DbSet<FaceComparisonDetection> FaceComparisonDetections => Set<FaceComparisonDetection>();

    public DbSet<FaceRecognitionComparisonRun> FaceRecognitionComparisonRuns => Set<FaceRecognitionComparisonRun>();
    public DbSet<FaceRecognitionComparisonResult> FaceRecognitionComparisonResults => Set<FaceRecognitionComparisonResult>();
    public DbSet<FaceRecognitionComparisonPair> FaceRecognitionComparisonPairs => Set<FaceRecognitionComparisonPair>();
    public DbSet<FaceRecognitionComparisonScore> FaceRecognitionComparisonScores => Set<FaceRecognitionComparisonScore>();
    public DbSet<FaceRecognitionComparisonEmbedding> FaceRecognitionComparisonEmbeddings => Set<FaceRecognitionComparisonEmbedding>();

    public DbSet<FaceIdentity> FaceIdentities => Set<FaceIdentity>();

    public DbSet<PersonReferenceFace> PersonReferenceFaces => Set<PersonReferenceFace>();

    public DbSet<FaceClusteringRun> FaceClusteringRuns => Set<FaceClusteringRun>();

    public DbSet<FaceCluster> FaceClusters => Set<FaceCluster>();

    public DbSet<IgnoredFaceGroup> IgnoredFaceGroups => Set<IgnoredFaceGroup>();

    public DbSet<VisualEmbedding> VisualEmbeddings => Set<VisualEmbedding>();

    public DbSet<LocationObservation> LocationObservations => Set<LocationObservation>();

    public DbSet<SceneObservation> SceneObservations => Set<SceneObservation>();

    public DbSet<SceneObservationReviewDecision> SceneObservationReviewDecisions => Set<SceneObservationReviewDecision>();

    public DbSet<EventClusteringRun> EventClusteringRuns => Set<EventClusteringRun>();

    public DbSet<EventCluster> EventClusters => Set<EventCluster>();

    public DbSet<EventClusterPhoto> EventClusterPhotos => Set<EventClusterPhoto>();

    public DbSet<EventCandidate> EventCandidates => Set<EventCandidate>();

    public DbSet<EventCandidateReviewDecision> EventCandidateReviewDecisions => Set<EventCandidateReviewDecision>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnowledgeBaseDbContext).Assembly);
}
