using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(t => t.UserId).HasColumnName("user_id").IsRequired();

        b.Property(t => t.TokenHash).HasColumnName("token_hash").HasColumnType("bytea").IsRequired();

        b.Property(t => t.FamilyId).HasColumnName("family_id").IsRequired();

        b.Property(t => t.IssuedAt)
            .HasColumnName("issued_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();

        b.Property(t => t.UsedAt).HasColumnName("used_at");
        b.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        b.Property(t => t.RevokedReason).HasColumnName("revoked_reason").HasColumnType("text");

        b.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        b.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family");
    }
}
