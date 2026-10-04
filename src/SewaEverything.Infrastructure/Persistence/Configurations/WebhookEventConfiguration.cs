using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("webhook_events");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(e => e.Provider).HasColumnName("provider").HasColumnType("text").IsRequired();
        b.Property(e => e.EventId).HasColumnName("event_id").HasColumnType("text").IsRequired();
        b.Property(e => e.Signature).HasColumnName("signature").HasColumnType("text");

        b.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        b.Property(e => e.ReceivedAt)
            .HasColumnName("received_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        b.Property(e => e.ProcessError).HasColumnName("process_error").HasColumnType("text");

        b.HasIndex(e => new { e.Provider, e.EventId }).IsUnique().HasDatabaseName("ux_webhook_events");
    }
}
