using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class NoteLinkConfiguration : IEntityTypeConfiguration<NoteLink>
{
    public void Configure(EntityTypeBuilder<NoteLink> builder)
    {
        builder.HasKey(link => link.Id);

        builder.Property(link => link.Id).HasMaxLength(32);
        builder.Property(link => link.SourceNoteId).HasMaxLength(32);
        builder.Property(link => link.TargetNoteId).HasMaxLength(32);
        builder.Property(link => link.TargetTitle).HasMaxLength(200);

        builder.HasIndex(link => new { link.SourceNoteId, link.TargetTitle }).IsUnique();

        // Backlinks: "which notes point here" by id, plus resolving dangling links by title
        // once the target note appears.
        builder.HasIndex(link => link.TargetNoteId);
        builder.HasIndex(link => link.TargetTitle);

        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(link => link.SourceNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(link => link.TargetNoteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
