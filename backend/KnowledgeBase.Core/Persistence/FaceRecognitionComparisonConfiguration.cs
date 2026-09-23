using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class FaceRecognitionComparisonRunConfiguration : IEntityTypeConfiguration<FaceRecognitionComparisonRun>
{
    public void Configure(EntityTypeBuilder<FaceRecognitionComparisonRun> builder)
    {
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasMaxLength(32);
        builder.Property(run => run.JobId).HasMaxLength(32);
        builder.HasIndex(run => run.CreatedAtUtc);
        builder.HasIndex(run => run.JobId).IsUnique();
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(run => run.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(run => run.Results).WithOne().HasForeignKey(result => result.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(run => run.Pairs).WithOne().HasForeignKey(pair => pair.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceRecognitionComparisonResultConfiguration : IEntityTypeConfiguration<FaceRecognitionComparisonResult>
{
    public void Configure(EntityTypeBuilder<FaceRecognitionComparisonResult> builder)
    {
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).HasMaxLength(32);
        builder.Property(result => result.RunId).HasMaxLength(32);
        builder.Property(result => result.ModelId).HasMaxLength(40);
        builder.Property(result => result.ModelName).HasMaxLength(160);
        builder.Property(result => result.ConfigurationJson).HasColumnType("jsonb");
        builder.Property(result => result.Error).HasMaxLength(2000);
        builder.HasIndex(result => new { result.RunId, result.ModelId }).IsUnique();
        builder.HasMany(result => result.Scores).WithOne().HasForeignKey(score => score.ResultId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceRecognitionComparisonEmbeddingConfiguration : IEntityTypeConfiguration<FaceRecognitionComparisonEmbedding>
{
    public void Configure(EntityTypeBuilder<FaceRecognitionComparisonEmbedding> builder)
    {
        builder.HasKey(embedding => embedding.Id);
        builder.Property(embedding => embedding.Id).HasMaxLength(32);
        builder.Property(embedding => embedding.RunId).HasMaxLength(32);
        builder.Property(embedding => embedding.FaceOccurrenceId).HasMaxLength(32);
        builder.Property(embedding => embedding.ModelId).HasMaxLength(40);
        builder.Property(embedding => embedding.Embedding).HasColumnType("real[]");
        builder.HasIndex(embedding => new { embedding.FaceOccurrenceId, embedding.ModelId }).IsUnique();
        builder.HasIndex(embedding => embedding.RunId);
        builder.HasOne<FaceRecognitionComparisonRun>().WithMany().HasForeignKey(embedding => embedding.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FaceOccurrence>().WithMany().HasForeignKey(embedding => embedding.FaceOccurrenceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceRecognitionComparisonPairConfiguration : IEntityTypeConfiguration<FaceRecognitionComparisonPair>
{
    public void Configure(EntityTypeBuilder<FaceRecognitionComparisonPair> builder)
    {
        builder.HasKey(pair => pair.Id);
        builder.Property(pair => pair.Id).HasMaxLength(32);
        builder.Property(pair => pair.RunId).HasMaxLength(32);
        builder.Property(pair => pair.FirstFaceOccurrenceId).HasMaxLength(32);
        builder.Property(pair => pair.SecondFaceOccurrenceId).HasMaxLength(32);
        builder.HasIndex(pair => new { pair.RunId, pair.FirstFaceOccurrenceId, pair.SecondFaceOccurrenceId }).IsUnique();
        builder.HasMany(pair => pair.Scores).WithOne().HasForeignKey(score => score.PairId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceRecognitionComparisonScoreConfiguration : IEntityTypeConfiguration<FaceRecognitionComparisonScore>
{
    public void Configure(EntityTypeBuilder<FaceRecognitionComparisonScore> builder)
    {
        builder.HasKey(score => score.Id);
        builder.Property(score => score.Id).HasMaxLength(32);
        builder.Property(score => score.PairId).HasMaxLength(32);
        builder.Property(score => score.ResultId).HasMaxLength(32);
        builder.HasIndex(score => new { score.PairId, score.ResultId }).IsUnique();
    }
}
