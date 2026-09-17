using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class EventClusteringRunConfiguration : IEntityTypeConfiguration<EventClusteringRun>
{
    public void Configure(EntityTypeBuilder<EventClusteringRun> builder)
    {
        builder.HasKey(run => run.Id); builder.Property(run => run.Id).HasMaxLength(32);
        builder.Property(run => run.PipelineVersion).HasMaxLength(100); builder.Property(run => run.ModelKey).HasMaxLength(200);
        builder.Property(run => run.ConfigurationHash).HasMaxLength(128);
        builder.HasIndex(run => run.CompletedAtUtc);
    }
}

internal sealed class EventClusterConfiguration : IEntityTypeConfiguration<EventCluster>
{
    public void Configure(EntityTypeBuilder<EventCluster> builder)
    {
        builder.HasKey(cluster => cluster.Id); builder.Property(cluster => cluster.Id).HasMaxLength(32);
        builder.Property(cluster => cluster.RunId).HasMaxLength(32); builder.Property(cluster => cluster.SignalsJson).HasColumnType("jsonb");
        builder.HasIndex(cluster => new { cluster.RunId, cluster.Score });
        builder.HasOne<EventClusteringRun>().WithMany().HasForeignKey(cluster => cluster.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventClusterPhotoConfiguration : IEntityTypeConfiguration<EventClusterPhoto>
{
    public void Configure(EntityTypeBuilder<EventClusterPhoto> builder)
    {
        builder.HasKey(photo => new { photo.ClusterId, photo.AssetId });
        builder.Property(photo => photo.ClusterId).HasMaxLength(32); builder.Property(photo => photo.AssetId).HasMaxLength(32);
        builder.HasIndex(photo => photo.AssetId);
        builder.HasOne<EventCluster>().WithMany().HasForeignKey(photo => photo.ClusterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(photo => photo.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventCandidateConfiguration : IEntityTypeConfiguration<EventCandidate>
{
    public void Configure(EntityTypeBuilder<EventCandidate> builder)
    {
        builder.HasKey(candidate => candidate.Id); builder.Property(candidate => candidate.Id).HasMaxLength(32);
        builder.Property(candidate => candidate.ClusterId).HasMaxLength(32);
        builder.HasIndex(candidate => new { candidate.ClusterId, candidate.CreatedAtUtc }).IsUnique();
        builder.HasIndex(candidate => new { candidate.SupersededAtUtc, candidate.CreatedAtUtc });
        builder.HasOne<EventCluster>().WithMany().HasForeignKey(candidate => candidate.ClusterId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventCandidateReviewDecisionConfiguration : IEntityTypeConfiguration<EventCandidateReviewDecision>
{
    public void Configure(EntityTypeBuilder<EventCandidateReviewDecision> builder)
    {
        builder.HasKey(decision => decision.Id); builder.Property(decision => decision.Id).HasMaxLength(32);
        builder.Property(decision => decision.CandidateId).HasMaxLength(32); builder.Property(decision => decision.ChosenEventId).HasMaxLength(32);
        builder.Property(decision => decision.Kind).HasConversion<string>().HasMaxLength(16); builder.Property(decision => decision.SelectedAssetIdsJson).HasColumnType("jsonb");
        builder.Property(decision => decision.Note).HasMaxLength(1000);
        builder.HasIndex(decision => decision.CandidateId).IsUnique();
        builder.HasOne<EventCandidate>().WithMany().HasForeignKey(decision => decision.CandidateId).OnDelete(DeleteBehavior.Cascade);
    }
}
