using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence.Configurations;

public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.ToTable("addresses");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();

        b.Property(a => a.Label).HasColumnName("label").IsRequired();
        b.Property(a => a.RecipientName).HasColumnName("recipient_name").IsRequired();
        b.Property(a => a.Phone).HasColumnName("phone").IsRequired();
        b.Property(a => a.FullAddress).HasColumnName("full_address").IsRequired();
        b.Property(a => a.Notes).HasColumnName("notes");

        b.Property(a => a.IsDefault).HasColumnName("is_default").HasDefaultValue(false);

        b.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        b.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        b.HasIndex(a => a.UserId).HasDatabaseName("ix_addresses_user");
    }
}
