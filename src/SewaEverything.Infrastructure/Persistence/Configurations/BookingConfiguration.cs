using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.ToTable("bookings");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(x => x.Reference)
            .HasColumnName("reference")
            .HasDefaultValueSql("gen_booking_reference()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(x => x.RenterId).HasColumnName("renter_id").IsRequired();

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

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .HasConversion(s => s.ToDbValue(), v => BookingStatuses.FromDbValue(v))
            .IsRequired();

        b.Property(x => x.PriceSnapshot)
            .HasColumnName("price_snapshot").HasColumnType("numeric(14,2)").IsRequired();

        b.Property(x => x.PriceUnitSnapshot)
            .HasColumnName("price_unit_snapshot")
            .HasColumnType("text")
            .HasConversion(u => u.ToDbValue(), v => PriceUnits.FromDbValue(v))
            .IsRequired();

        b.Property(x => x.DurationUnits).HasColumnName("duration_units").IsRequired();

        b.Property(x => x.TotalRent)
            .HasColumnName("total_rent").HasColumnType("numeric(14,2)").IsRequired();

        b.Property(x => x.DepositAmount)
            .HasColumnName("deposit_amount").HasColumnType("numeric(14,2)").IsRequired();

        b.Property(x => x.PlatformFeeRate)
            .HasColumnName("platform_fee_rate").HasColumnType("numeric(6,4)").IsRequired();

        b.Property(x => x.PlatformFeeMode)
            .HasColumnName("platform_fee_mode")
            .HasColumnType("text")
            .HasConversion(m => m.ToDbValue(), v => CommissionModes.FromDbValue(v))
            .IsRequired();

        b.Property(x => x.PlatformFeeAmount)
            .HasColumnName("platform_fee_amount").HasColumnType("numeric(14,2)").IsRequired();

        b.Property(x => x.DeliveryMethod)
            .HasColumnName("delivery_method")
            .HasColumnType("text")
            .HasConversion(m => m.ToDbValue(), v => DeliveryMethods.FromDbValue(v))
            .IsRequired();

        b.Property(x => x.DeliveryFee)
            .HasColumnName("delivery_fee").HasColumnType("numeric(12,2)").IsRequired();

        b.Property(x => x.DeliveryRecipient).HasColumnName("delivery_recipient").HasColumnType("text");
        b.Property(x => x.DeliveryPhone).HasColumnName("delivery_phone").HasColumnType("text");
        b.Property(x => x.DeliveryAddress).HasColumnName("delivery_address").HasColumnType("text");
        b.Property(x => x.DeliveryNotes).HasColumnName("delivery_notes").HasColumnType("text");

        b.Property(x => x.RenterTotal)
            .HasColumnName("renter_total")
            .HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.RenterTotal).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        b.Property(x => x.RenterTotal).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property(x => x.SellerGross)
            .HasColumnName("seller_gross")
            .HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.SellerGross).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        b.Property(x => x.SellerGross).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property(x => x.HoldExpiresAt).HasColumnName("hold_expires_at");
        b.Property(x => x.CancelledReason).HasColumnName("cancelled_reason").HasColumnType("text");

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasOne(x => x.Item)
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Renter)
            .WithMany()
            .HasForeignKey(x => x.RenterId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.RenterId, x.CreatedAt }).HasDatabaseName("ix_bookings_renter");
        b.HasIndex(x => new { x.ItemId, x.StartsAt }).HasDatabaseName("ix_bookings_item");
    }
}
