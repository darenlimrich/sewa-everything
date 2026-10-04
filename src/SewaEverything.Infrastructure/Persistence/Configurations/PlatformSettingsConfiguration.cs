using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> b)
    {
        b.ToTable("platform_settings");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        b.Property(p => p.CommissionRate)
            .HasColumnName("commission_rate")
            .HasColumnType("numeric(6,4)")
            .IsRequired();

        b.Property(p => p.CommissionMode)
            .HasColumnName("commission_mode")
            .HasColumnType("text")
            .HasConversion(m => m.ToDbValue(), v => CommissionModes.FromDbValue(v))
            .IsRequired();

        b.Property(p => p.ApprovalMinutes).HasColumnName("approval_minutes").IsRequired();
        b.Property(p => p.PaymentMinutes).HasColumnName("payment_minutes").IsRequired();
        b.Property(p => p.ReturnWindowDays).HasColumnName("return_window_days").IsRequired();
        b.Property(p => p.MidtransServerKey)
            .HasColumnName("midtrans_server_key").HasColumnType("text");

        b.Property(p => p.MidtransClientKey)
            .HasColumnName("midtrans_client_key").HasColumnType("text");

        b.Property(p => p.MidtransIsProduction)
            .HasColumnName("midtrans_is_production").IsRequired();

        b.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        b.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
