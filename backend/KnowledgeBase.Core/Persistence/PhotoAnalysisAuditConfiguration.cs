using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class PhotoAnalysisRunConfiguration : IEntityTypeConfiguration<PhotoAnalysisRun>
{
    public void Configure(EntityTypeBuilder<PhotoAnalysisRun> builder)
    {
        builder.HasKey(run => run.Id); builder.Property(run => run.Id).HasMaxLength(32);
        builder.Property(run => run.AssetId).HasMaxLength(32); builder.Property(run => run.PipelineVersion).HasMaxLength(100);
        builder.Property(run => run.ModelKey).HasMaxLength(200); builder.Property(run => run.ConfigurationHash).HasMaxLength(128);
        builder.HasIndex(run => new { run.AssetId, run.CompletedAtUtc });
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(run => run.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PhotoAnalysisCandidateConfiguration : IEntityTypeConfiguration<PhotoAnalysisCandidate>
{
    public void Configure(EntityTypeBuilder<PhotoAnalysisCandidate> builder)
    {
        builder.HasKey(candidate => candidate.Id); builder.Property(candidate => candidate.Id).HasMaxLength(32);
        builder.Property(candidate => candidate.RunId).HasMaxLength(32); builder.Property(candidate => candidate.SubjectAssetId).HasMaxLength(32);
        builder.Property(candidate => candidate.SubjectFaceOccurrenceId).HasMaxLength(32);
        builder.Property(candidate => candidate.ProposedTargetId).HasMaxLength(32); builder.Property(candidate => candidate.ProposedLabel).HasMaxLength(200);
        builder.Property(candidate => candidate.Kind).HasConversion<string>().HasMaxLength(16); builder.Property(candidate => candidate.SignalsJson).HasColumnType("jsonb");
        builder.HasIndex(candidate => new { candidate.RunId, candidate.SubjectFaceOccurrenceId, candidate.Kind, candidate.Rank }).IsUnique();
        builder.HasIndex(candidate => new { candidate.Kind, candidate.SubjectAssetId, candidate.SupersededAtUtc });
        builder.HasOne<PhotoAnalysisRun>().WithMany().HasForeignKey(candidate => candidate.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(candidate => candidate.SubjectAssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FaceOccurrence>().WithMany().HasForeignKey(candidate => candidate.SubjectFaceOccurrenceId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class FaceOccurrenceConfiguration : IEntityTypeConfiguration<FaceOccurrence>
{
    public void Configure(EntityTypeBuilder<FaceOccurrence> builder)
    {
        builder.HasKey(occurrence => occurrence.Id); builder.Property(occurrence => occurrence.Id).HasMaxLength(32);
        builder.Property(occurrence => occurrence.RunId).HasMaxLength(32); builder.Property(occurrence => occurrence.AssetId).HasMaxLength(32);
        builder.Property(occurrence => occurrence.LandmarksJson).HasColumnType("jsonb"); builder.Property(occurrence => occurrence.Embedding).HasColumnType("real[]");
        builder.HasIndex(occurrence => new { occurrence.AssetId, occurrence.CreatedAtUtc });
        builder.HasOne<PhotoAnalysisRun>().WithMany().HasForeignKey(occurrence => occurrence.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(occurrence => occurrence.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonReferenceFaceConfiguration : IEntityTypeConfiguration<PersonReferenceFace>
{
    public void Configure(EntityTypeBuilder<PersonReferenceFace> builder)
    {
        builder.HasKey(reference => reference.FaceOccurrenceId);
        builder.Property(reference => reference.PersonId).HasMaxLength(32); builder.Property(reference => reference.FaceOccurrenceId).HasMaxLength(32); builder.Property(reference => reference.SourceDecisionId).HasMaxLength(32);
        builder.HasIndex(reference => reference.PersonId);
        builder.HasOne<Person>().WithMany().HasForeignKey(reference => reference.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FaceOccurrence>().WithMany().HasForeignKey(reference => reference.FaceOccurrenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PhotoAnalysisReviewDecision>().WithMany().HasForeignKey(reference => reference.SourceDecisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VisualEmbeddingConfiguration : IEntityTypeConfiguration<VisualEmbedding>
{
    public void Configure(EntityTypeBuilder<VisualEmbedding> builder)
    {
        builder.HasKey(embedding => embedding.Id); builder.Property(embedding => embedding.Id).HasMaxLength(32);
        builder.Property(embedding => embedding.RunId).HasMaxLength(32); builder.Property(embedding => embedding.AssetId).HasMaxLength(32);
        builder.Property(embedding => embedding.Embedding).HasColumnType("real[]");
        builder.HasIndex(embedding => new { embedding.AssetId, embedding.CreatedAtUtc });
        builder.HasIndex(embedding => embedding.RunId).IsUnique();
        builder.HasOne<PhotoAnalysisRun>().WithMany().HasForeignKey(embedding => embedding.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(embedding => embedding.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LocationObservationConfiguration : IEntityTypeConfiguration<LocationObservation>
{
    public void Configure(EntityTypeBuilder<LocationObservation> builder)
    {
        builder.HasKey(observation => observation.Id); builder.Property(observation => observation.Id).HasMaxLength(32);
        builder.Property(observation => observation.LocationId).HasMaxLength(32); builder.Property(observation => observation.AssetId).HasMaxLength(32);
        builder.Property(observation => observation.VisualEmbeddingId).HasMaxLength(32); builder.Property(observation => observation.SourceDecisionId).HasMaxLength(32);
        builder.HasIndex(observation => observation.LocationId);
        builder.HasIndex(observation => observation.VisualEmbeddingId).IsUnique();
        builder.HasIndex(observation => observation.SourceDecisionId).IsUnique();
        builder.HasOne<Location>().WithMany().HasForeignKey(observation => observation.LocationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(observation => observation.AssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VisualEmbedding>().WithMany().HasForeignKey(observation => observation.VisualEmbeddingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PhotoAnalysisReviewDecision>().WithMany().HasForeignKey(observation => observation.SourceDecisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SceneObservationConfiguration : IEntityTypeConfiguration<SceneObservation>
{
    public void Configure(EntityTypeBuilder<SceneObservation> builder)
    {
        builder.HasKey(observation => observation.Id); builder.Property(observation => observation.Id).HasMaxLength(32);
        builder.Property(observation => observation.RunId).HasMaxLength(32); builder.Property(observation => observation.AssetId).HasMaxLength(32);
        builder.Property(observation => observation.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(observation => observation.SubjectPersonId).HasMaxLength(32); builder.Property(observation => observation.SubjectPersonName).HasMaxLength(160);
        builder.Property(observation => observation.RelatedPersonId).HasMaxLength(32); builder.Property(observation => observation.RelatedPersonName).HasMaxLength(160);
        builder.Property(observation => observation.Description).HasMaxLength(500); builder.Property(observation => observation.Evidence).HasMaxLength(500);
        builder.HasIndex(observation => new { observation.AssetId, observation.SupersededAtUtc });
        builder.HasIndex(observation => observation.RunId);
        builder.HasOne<PhotoAnalysisRun>().WithMany().HasForeignKey(observation => observation.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(observation => observation.AssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Person>().WithMany().HasForeignKey(observation => observation.SubjectPersonId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Person>().WithMany().HasForeignKey(observation => observation.RelatedPersonId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SceneObservationReviewDecisionConfiguration : IEntityTypeConfiguration<SceneObservationReviewDecision>
{
    public void Configure(EntityTypeBuilder<SceneObservationReviewDecision> builder)
    {
        builder.HasKey(decision => decision.Id); builder.Property(decision => decision.Id).HasMaxLength(32);
        builder.Property(decision => decision.ObservationId).HasMaxLength(32); builder.Property(decision => decision.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(decision => decision.Note).HasMaxLength(1000);
        builder.HasIndex(decision => decision.ObservationId).IsUnique();
        builder.HasOne<SceneObservation>().WithMany().HasForeignKey(decision => decision.ObservationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PhotoAnalysisReviewDecisionConfiguration : IEntityTypeConfiguration<PhotoAnalysisReviewDecision>
{
    public void Configure(EntityTypeBuilder<PhotoAnalysisReviewDecision> builder)
    {
        builder.HasKey(decision => decision.Id); builder.Property(decision => decision.Id).HasMaxLength(32);
        builder.Property(decision => decision.CandidateId).HasMaxLength(32); builder.Property(decision => decision.ChosenTargetId).HasMaxLength(32);
        builder.Property(decision => decision.Kind).HasConversion<string>().HasMaxLength(16); builder.Property(decision => decision.Note).HasMaxLength(1000);
        builder.HasIndex(decision => new { decision.CandidateId, decision.DecidedAtUtc });
        builder.HasOne<PhotoAnalysisCandidate>().WithMany().HasForeignKey(decision => decision.CandidateId).OnDelete(DeleteBehavior.Cascade);
    }
}
