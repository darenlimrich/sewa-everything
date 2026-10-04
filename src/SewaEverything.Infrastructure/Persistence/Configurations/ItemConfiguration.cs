using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public const string SearchVectorProperty = "SearchVector";

    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.HasKey(i => i.Id);

        b.Property(i => i.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(i => i.SellerId).HasColumnName("seller_id").IsRequired();

        b.Property(i => i.Title).HasColumnName("title").HasColumnType("text").IsRequired();
        b.Property(i => i.Category).HasColumnName("category").HasColumnType("text").IsRequired();
        b.Property(i => i.Description).HasColumnName("description").HasColumnType("text");

        b.Property(i => i.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(14,2)")
            .IsRequired();

        b.Property(i => i.PriceUnit)
            .HasColumnName("price_unit")
            .HasColumnType("text")
            .HasConversion(u => u.ToDbValue(), v => PriceUnits.FromDbValue(v))
            .IsRequired();

        b.Property(i => i.DepositAmount)
            .HasColumnName("deposit_amount")
            .HasColumnType("numeric(14,2)")
            .IsRequired();


        b.Property(i => i.DeliveryFee)

            .HasColumnName("delivery_fee").HasColumnType("numeric(12,2)");

        b.Property(i => i.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .HasConversion(s => s.ToDbValue(), v => ItemStatuses.FromDbValue(v))
            .IsRequired();

        b.Property(i => i.SuspendedAt).HasColumnName("suspended_at");
        b.Property(i => i.SuspendedBy).HasColumnName("suspended_by");
        b.Property(i => i.SuspensionReason).HasColumnName("suspension_reason").HasColumnType("text");

        b.Property(i => i.ReviewStatus)
            .HasColumnName("review_status")
            .HasColumnType("text")
            .HasConversion(s => s.ToDbValue(), v => ItemReviewStatuses.FromDbValue(v))
            .IsRequired();

        b.Property(i => i.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(i => i.ReviewedBy).HasColumnName("reviewed_by");
        b.Property(i => i.RejectionReason).HasColumnName("rejection_reason").HasColumnType("text");

        b.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property<NpgsqlTsVector>(SearchVectorProperty)
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property<NpgsqlTsVector>(SearchVectorProperty)
            .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasOne(i => i.Seller)
            .WithMany()
            .HasForeignKey(i => i.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(i => i.Photos)
            .WithOne()
            .HasForeignKey(p => p.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(i => i.SellerId).HasDatabaseName("ix_items_seller");
    }
}
