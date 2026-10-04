using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> b)
    {
        b.ToTable("disputes");
        b.HasKey(d => d.Id);

        b.Property(d => d.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(d => d.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(d => d.RaisedBy).HasColumnName("raised_by").IsRequired();
        b.Property(d => d.Reason).HasColumnName("reason").HasColumnType("text").IsRequired();

        b.Property(d => d.Status)
            .HasColumnName("status").HasColumnType("text")
            .HasConversion(s => s.ToDbValue(), v => DisputeStatuses.FromDbValue(v))
            .IsRequired();

        b.Property(d => d.Resolution).HasColumnName("resolution").HasColumnType("text");
        b.Property(d => d.ResolvedBy).HasColumnName("resolved_by");
        b.Property(d => d.ResolvedAt).HasColumnName("resolved_at");

        b.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasOne(d => d.Booking)
            .WithMany()
            .HasForeignKey(d => d.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(d => d.BookingId).IsUnique().HasDatabaseName("disputes_booking_id_key");
    }
}
