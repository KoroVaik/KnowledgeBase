using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id).HasMaxLength(32);
        builder.Property(job => job.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(job => job.AssetId).HasMaxLength(32);
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(job => job.Error).HasMaxLength(2000);

        // One job of each kind per asset: source-note and face analysis may run independently.
        // Aggregation jobs have no asset, so the constraint only covers the rows that carry one.
        builder.HasIndex(job => new { job.AssetId, job.Kind })
            .IsUnique()
            .HasFilter("\"AssetId\" IS NOT NULL");

        // The worker polls WHERE Status = 'Pending' ORDER BY CreatedAtUtc.
        builder.HasIndex(job => new { job.Status, job.CreatedAtUtc });

        builder.HasOne<AssetRecord>()
            .WithMany()
            .HasForeignKey(job => job.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
