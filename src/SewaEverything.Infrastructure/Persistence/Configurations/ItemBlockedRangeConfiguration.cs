using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class ItemBlockedRangeConfiguration : IEntityTypeConfiguration<ItemBlockedRange>
{
    public void Configure(EntityTypeBuilder<ItemBlockedRange> b)
    {
        b.HasNoKey();
        b.ToView("item_blocked_ranges");

        b.Property(r => r.ItemId).HasColumnName("item_id");
        b.Property(r => r.StartsAt).HasColumnName("starts_at");
        b.Property(r => r.EndsAt).HasColumnName("ends_at");
        b.Property(r => r.SourceId).HasColumnName("source_id");

        b.Property(r => r.Source)
            .HasColumnName("source")
            .HasColumnType("text")
            .HasConversion(s => s.ToDbValue(), v => BlockedRangeSources.FromDbValue(v));
    }
}
