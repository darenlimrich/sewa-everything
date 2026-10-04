using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Persistence;

public sealed class SewaDbContext(DbContextOptions<SewaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<ItemPhoto> ItemPhotos => Set<ItemPhoto>();

    public DbSet<ItemBlackout> ItemBlackouts => Set<ItemBlackout>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PayoutAccount> PayoutAccounts => Set<PayoutAccount>();

    public DbSet<Dispute> Disputes => Set<Dispute>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    public DbSet<UserTotp> UserTotps => Set<UserTotp>();

    public DbSet<TotpRecoveryCode> TotpRecoveryCodes => Set<TotpRecoveryCode>();

    public DbSet<ItemBlockedRange> ItemBlockedRanges => Set<ItemBlockedRange>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Address> Addresses => Set<Address>();

    public void SetDuring<TEntity>(TEntity entity, DateTime from, DateTime to)
        where TEntity : class
    {
        Entry(entity).Property(RangeProperties.During).CurrentValue =
            new NpgsqlRange<DateTime>(
                from, lowerBoundIsInclusive: true,
                to,   upperBoundIsInclusive: false);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(SewaDbContext).Assembly);
    }
}
