using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class FaceComparisonRunConfiguration : IEntityTypeConfiguration<FaceComparisonRun>
{
    public void Configure(EntityTypeBuilder<FaceComparisonRun> builder)
    {
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasMaxLength(32);
        builder.Property(run => run.AssetId).HasMaxLength(32);
        builder.Property(run => run.JobId).HasMaxLength(32);
        builder.Property(run => run.ContentSha256).HasMaxLength(64);
        builder.HasIndex(run => new { run.AssetId, run.CreatedAtUtc });
        builder.HasIndex(run => run.JobId).IsUnique();
        builder.HasOne<AssetRecord>().WithMany().HasForeignKey(run => run.AssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(run => run.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(run => run.Results).WithOne().HasForeignKey(result => result.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceComparisonResultConfiguration : IEntityTypeConfiguration<FaceComparisonResult>
{
    public void Configure(EntityTypeBuilder<FaceComparisonResult> builder)
    {
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).HasMaxLength(32);
        builder.Property(result => result.RunId).HasMaxLength(32);
        builder.Property(result => result.ModelId).HasMaxLength(40);
        builder.Property(result => result.ModelName).HasMaxLength(160);
        builder.Property(result => result.ConfigurationJson).HasColumnType("jsonb");
        builder.Property(result => result.Error).HasMaxLength(2000);
        builder.HasIndex(result => new { result.RunId, result.ModelId }).IsUnique();
        builder.HasMany(result => result.Detections).WithOne().HasForeignKey(face => face.ResultId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceComparisonDetectionConfiguration : IEntityTypeConfiguration<FaceComparisonDetection>
{
    public void Configure(EntityTypeBuilder<FaceComparisonDetection> builder)
    {
        builder.HasKey(face => face.Id);
        builder.Property(face => face.Id).HasMaxLength(32);
        builder.Property(face => face.ResultId).HasMaxLength(32);
        builder.Property(face => face.LandmarksJson).HasColumnType("jsonb");
        builder.Property(face => face.WarningsJson).HasColumnType("jsonb");
        builder.HasIndex(face => new { face.ResultId, face.Ordinal }).IsUnique();
    }
}
