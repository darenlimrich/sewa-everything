using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(p => p.Reference)
            .HasColumnName("reference")
            .HasDefaultValueSql("gen_payment_reference()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.Property(p => p.BookingId).HasColumnName("booking_id").IsRequired();

        b.Property(p => p.Kind)
            .HasColumnName("kind").HasColumnType("text")
            .HasConversion(k => k.ToDbValue(), v => PaymentKinds.FromDbValue(v))
            .IsRequired();

        b.Property(p => p.Direction)
            .HasColumnName("direction").HasColumnType("text")
            .HasConversion(d => d.ToDbValue(), v => PaymentDirections.FromDbValue(v))
            .IsRequired();

        b.Property(p => p.Amount)
            .HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();

        b.Property(p => p.Currency)
            .HasColumnName("currency").HasColumnType("char(3)").IsRequired();

        b.Property(p => p.Status)
            .HasColumnName("status").HasColumnType("text")
            .HasConversion(s => s.ToDbValue(), v => PaymentStatuses.FromDbValue(v))
            .IsRequired();

        b.Property(p => p.Method)
            .HasColumnName("method").HasColumnType("text")
            .HasConversion(m => m.ToDbValue(), v => PaymentMethods.FromDbValue(v))
            .IsRequired();

        b.Property(p => p.Channel).HasColumnName("channel").HasColumnType("text");
        b.Property(p => p.CounterpartyId).HasColumnName("counterparty_id");
        b.Property(p => p.PayoutAccountId).HasColumnName("payout_account_id");
        b.Property(p => p.GatewayRef).HasColumnName("gateway_ref").HasColumnType("text");
        b.Property(p => p.ParentId).HasColumnName("parent_id");

        b.Property(p => p.IdempotencyKey)
            .HasColumnName("idempotency_key").HasColumnType("text").IsRequired();

        b.Property(p => p.GatewayInstructions)
            .HasColumnName("gateway_instructions").HasColumnType("jsonb");

        b.Property(p => p.FailureReason).HasColumnName("failure_reason").HasColumnType("text");
        b.Property(p => p.SettledAt).HasColumnName("settled_at");

        b.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasIndex(p => p.IdempotencyKey).IsUnique().HasDatabaseName("payments_idempotency_key_key");

        b.HasIndex(p => new { p.BookingId, p.CreatedAt }).HasDatabaseName("ix_payments_booking");
    }
}
