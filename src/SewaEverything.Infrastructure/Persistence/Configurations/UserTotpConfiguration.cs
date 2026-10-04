using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class UserTotpConfiguration : IEntityTypeConfiguration<UserTotp>
{
    public void Configure(EntityTypeBuilder<UserTotp> b)
    {
        b.ToTable("user_totp");
        b.HasKey(t => t.UserId);

        b.Property(t => t.UserId).HasColumnName("user_id").ValueGeneratedNever();

        b.Property(t => t.Secret).HasColumnName("secret").HasColumnType("text").IsRequired();

        b.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(t => t.ConfirmedAt).HasColumnName("confirmed_at");

        b.Property(t => t.LastStep).HasColumnName("last_step");

        b.Ignore(t => t.IsConfirmed);

        b.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TotpRecoveryCodeConfiguration : IEntityTypeConfiguration<TotpRecoveryCode>
{
    public void Configure(EntityTypeBuilder<TotpRecoveryCode> b)
    {
        b.ToTable("totp_recovery_codes");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();

        b.Property(c => c.CodeHash).HasColumnName("code_hash").HasColumnType("bytea").IsRequired();

        b.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(c => c.UsedAt).HasColumnName("used_at");

        b.Ignore(c => c.IsLive);

        b.HasOne<UserTotp>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(c => c.CodeHash).IsUnique().HasDatabaseName("ux_totp_recovery_codes_hash");
    }
}
