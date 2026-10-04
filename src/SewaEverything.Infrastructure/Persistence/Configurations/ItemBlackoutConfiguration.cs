using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class ItemBlackoutConfiguration : IEntityTypeConfiguration<ItemBlackout>
{
    public void Configure(EntityTypeBuilder<ItemBlackout> b)
    {
        b.ToTable("item_blackouts");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasColumnType("text");

        b.Property<NpgsqlRange<DateTime>>(RangeProperties.During)
            .HasColumnName("during")
            .HasColumnType("tstzrange")
            .IsRequired();

        b.Property(x => x.StartsAt)
            .HasColumnName("starts_at")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property(x => x.EndsAt)
            .HasColumnName("ends_at")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.HasIndex(x => new { x.ItemId, x.StartsAt, x.EndsAt })
            .HasDatabaseName("ix_item_blackouts_window");
    }
}
