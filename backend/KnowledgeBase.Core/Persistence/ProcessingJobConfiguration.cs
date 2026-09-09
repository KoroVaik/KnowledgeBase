using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id).HasMaxLength(32);
        builder.Property(job => job.AssetId).HasMaxLength(32);
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(job => job.Error).HasMaxLength(2000);

        // One job per asset: re-enqueuing the same upload must not spawn a second worker run.
        builder.HasIndex(job => job.AssetId).IsUnique();

        // The worker polls WHERE Status = 'Pending' ORDER BY CreatedAtUtc.
        builder.HasIndex(job => new { job.Status, job.CreatedAtUtc });

        builder.HasOne<AssetRecord>()
            .WithMany()
            .HasForeignKey(job => job.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
