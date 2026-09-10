using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class SynthesisSourceConfiguration : IEntityTypeConfiguration<SynthesisSource>
{
    public void Configure(EntityTypeBuilder<SynthesisSource> builder)
    {
        // The pair is the key: one edge per (synthesis, input).
        builder.HasKey(edge => new { edge.SynthesisNoteId, edge.InputNoteId });

        builder.Property(edge => edge.SynthesisNoteId).HasMaxLength(32);
        builder.Property(edge => edge.InputNoteId).HasMaxLength(32);

        // "which synthesis notes was this note fed into" - the staleness query.
        builder.HasIndex(edge => edge.InputNoteId);

        // Purging either end drops the edge; a binned note keeps its edges (soft delete does
        // not cascade), which is what the staleness check wants.
        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(edge => edge.SynthesisNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(edge => edge.InputNoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
