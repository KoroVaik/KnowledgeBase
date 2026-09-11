using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class TagParentConfiguration : IEntityTypeConfiguration<TagParent>
{
    public void Configure(EntityTypeBuilder<TagParent> builder)
    {
        // The pair is the key: the same parent cannot be linked twice.
        builder.HasKey(link => new { link.ChildId, link.ParentId });

        builder.Property(link => link.ChildId).HasMaxLength(32);
        builder.Property(link => link.ParentId).HasMaxLength(32);

        // "every parent of X" and "every child of X" - both directions are queried.
        builder.HasIndex(link => link.ParentId);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(link => link.ChildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(link => link.ParentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
