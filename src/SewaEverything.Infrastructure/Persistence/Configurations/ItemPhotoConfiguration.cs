using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class ItemPhotoConfiguration : IEntityTypeConfiguration<ItemPhoto>
{
    public void Configure(EntityTypeBuilder<ItemPhoto> b)
    {
        b.ToTable("item_photos");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(p => p.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(p => p.Url).HasColumnName("url").HasColumnType("text").IsRequired();
        b.Property(p => p.SortOrder).HasColumnName("sort_order").IsRequired();

        b.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.HasIndex(p => new { p.ItemId, p.SortOrder }).HasDatabaseName("ix_item_photos_item");
    }
}
