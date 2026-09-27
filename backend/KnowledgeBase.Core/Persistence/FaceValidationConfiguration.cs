using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class FaceValidationConfiguration : IEntityTypeConfiguration<FaceValidation>
{
    public void Configure(EntityTypeBuilder<FaceValidation> builder)
    {
        builder.HasKey(item => item.FaceOccurrenceId);
        builder.Property(item => item.Subject).HasConversion<string>();
        builder.Property(item => item.Evidence).HasMaxLength(1000);
        builder.Property(item => item.LastError).HasMaxLength(1000);
        builder.HasOne<FaceOccurrence>().WithOne().HasForeignKey<FaceValidation>(item => item.FaceOccurrenceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FaceValidationReviewDecisionConfiguration : IEntityTypeConfiguration<FaceValidationReviewDecision>
{
    public void Configure(EntityTypeBuilder<FaceValidationReviewDecision> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Kind).HasConversion<string>();
        builder.HasIndex(item => new { item.FaceIdentityId, item.DecidedAtUtc });
        builder.HasOne<FaceIdentity>().WithMany().HasForeignKey(item => item.FaceIdentityId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FaceOccurrence>().WithMany().HasForeignKey(item => item.FaceOccurrenceId).OnDelete(DeleteBehavior.Cascade);
    }
}
