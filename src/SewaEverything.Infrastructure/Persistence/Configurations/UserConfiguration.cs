using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);

        b.Property(u => u.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(u => u.Role)
            .HasColumnName("role")
            .HasColumnType("text")
            .HasConversion(r => r.ToDbValue(), v => Roles.FromDbValue(v))
            .IsRequired();

        b.Property(u => u.Name).HasColumnName("name").HasColumnType("text").IsRequired();
        b.Property(u => u.Email).HasColumnName("email").HasColumnType("text").IsRequired();
        b.Property(u => u.Phone).HasColumnName("phone").HasColumnType("text");
        b.Property(u => u.AvatarUrl).HasColumnName("avatar_url").HasColumnType("text");

        b.Property(u => u.PasswordHash)
            .HasColumnName("password_hash").HasColumnType("text").IsRequired();

        b.Property(u => u.IsVerified).HasColumnName("is_verified").IsRequired();
        b.Property(u => u.VerifiedAt).HasColumnName("verified_at");
        b.Property(u => u.VerifiedBy).HasColumnName("verified_by");

        b.Property(u => u.DeactivatedAt).HasColumnName("deactivated_at");

        b.Property(u => u.FailedLoginCount).HasColumnName("failed_login_count").IsRequired();
        b.Property(u => u.LastFailedLoginAt).HasColumnName("last_failed_login_at");
        b.Property(u => u.LockedUntil).HasColumnName("locked_until");

        b.Property(u => u.NotificationsSeenAt).HasColumnName("notifications_seen_at");

        b.Ignore(u => u.IsActive);

        b.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasIndex(u => u.Role).HasDatabaseName("ix_users_role");
    }
}
