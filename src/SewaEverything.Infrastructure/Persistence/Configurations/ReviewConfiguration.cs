using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(r => r.BookingId).HasColumnName("booking_id").IsRequired();

        b.Property(r => r.Rating).HasColumnName("rating").HasColumnType("smallint").IsRequired();

        b.Property(r => r.Comment).HasColumnName("comment").HasColumnType("text");

        b.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.HasOne(r => r.Booking)
            .WithMany()
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(r => r.BookingId).IsUnique().HasDatabaseName("reviews_booking_id_key");
    }
}
