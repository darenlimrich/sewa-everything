using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class PayoutAccountConfiguration : IEntityTypeConfiguration<PayoutAccount>
{
    public void Configure(EntityTypeBuilder<PayoutAccount> b)
    {
        b.ToTable("payout_accounts");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();

        b.Property(a => a.Kind)
            .HasColumnName("kind").HasColumnType("text")
            .HasConversion(k => k.ToDbValue(), v => PayoutAccountKinds.FromDbValue(v))
            .IsRequired();

        b.Property(a => a.ProviderCode)
            .HasColumnName("provider_code").HasColumnType("text").IsRequired();
        b.Property(a => a.AccountNumber)
            .HasColumnName("account_number").HasColumnType("text").IsRequired();
        b.Property(a => a.AccountHolder)
            .HasColumnName("account_holder").HasColumnType("text").IsRequired();

        b.Property(a => a.IsDefault).HasColumnName("is_default").IsRequired();
        b.Property(a => a.VerifiedAt).HasColumnName("verified_at");

        b.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasIndex(a => a.UserId).HasDatabaseName("ix_payout_accounts_user");
    }
}
