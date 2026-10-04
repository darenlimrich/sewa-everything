using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class PasswordResetConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> b)
    {
        b.ToTable("password_resets");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();

        b.Property(r => r.TokenHash).HasColumnName("token_hash").HasColumnType("bytea").IsRequired();

        b.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();

        b.Property(r => r.UsedAt).HasColumnName("used_at");

        b.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("ux_password_resets_hash");
    }
}
