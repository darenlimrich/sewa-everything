using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("cart_items");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(c => c.RenterId).HasColumnName("renter_id").IsRequired();
        b.Property(c => c.ItemId).HasColumnName("item_id").IsRequired();

        b.Property(c => c.StartAt).HasColumnName("start_at").IsRequired();
        b.Property(c => c.EndAt).HasColumnName("end_at").IsRequired();

        b.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasOne(c => c.Item).WithMany().HasForeignKey(c => c.ItemId);

        b.HasIndex(c => new { c.RenterId, c.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_cart_items_renter_item");
    }
}
