using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasKey(tag => tag.Id);

        builder.Property(tag => tag.Id).HasMaxLength(32);
        builder.Property(tag => tag.Name).HasMaxLength(100);

        // One tag per name - the vocabulary, and links/facets resolve by name.
        builder.HasIndex(tag => tag.Name).IsUnique();
    }
}
