using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Core.Persistence;

internal sealed class AssetRecordConfiguration : IEntityTypeConfiguration<AssetRecord>
{
    public void Configure(EntityTypeBuilder<AssetRecord> builder)
    {
        builder.HasKey(asset => asset.Id);

        builder.HasIndex(asset => asset.StoredFileName).IsUnique();

        builder.Property(asset => asset.Id).HasMaxLength(32);
        builder.Property(asset => asset.StoredFileName).HasMaxLength(64);
        builder.Property(asset => asset.OriginalFileName).HasMaxLength(255);
        builder.Property(asset => asset.ContentType).HasMaxLength(255);
    }
}
