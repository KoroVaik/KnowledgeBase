using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class NoteTagConfiguration : IEntityTypeConfiguration<NoteTag>
{
    public void Configure(EntityTypeBuilder<NoteTag> builder)
    {
        // The pair is the key: the DB refuses the same tag on a note twice, no extra index.
        builder.HasKey(link => new { link.NoteId, link.TagId });

        builder.Property(link => link.NoteId).HasMaxLength(32);
        builder.Property(link => link.TagId).HasMaxLength(32);

        // Facet query: every note carrying tag X.
        builder.HasIndex(link => link.TagId);

        builder.HasOne<Note>()
            .WithMany()
            .HasForeignKey(link => link.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(link => link.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
