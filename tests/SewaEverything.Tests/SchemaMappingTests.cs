using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class SchemaMappingTests(ApiFactory api)
{
    private SewaDbContext NewDb(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<SewaDbContext>();

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Seller)]
    [InlineData(UserRole.Renter)]
    public async Task Setiap_role_diterima_database(UserRole role)
    {
        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Role         = role,
            Name         = $"Uji {role}",
            Email        = ApiFactory.UniqueEmail($"role-{role}"),
            PasswordHash = hasher.Hash(ApiFactory.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tersimpan = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(role, tersimpan.Role);
    }

    [Theory]
    [InlineData(UserRole.Owner,  Roles.Owner)]
    [InlineData(UserRole.Admin,  Roles.Admin)]
    [InlineData(UserRole.Seller, Roles.Seller)]
    [InlineData(UserRole.Renter, Roles.Renter)]
    public void Enum_dan_konstanta_role_bolak_balik_konsisten(UserRole role, string konstanta)
    {
        Assert.Equal(konstanta, role.ToDbValue());
        Assert.Equal(role, Roles.FromDbValue(konstanta));
        Assert.Equal(konstanta, role.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData(CommissionMode.Deduct, CommissionModes.Deduct)]
    [InlineData(CommissionMode.OnTop,  CommissionModes.OnTop)]
    public void Enum_dan_konstanta_mode_komisi_bolak_balik_konsisten(CommissionMode mode, string konstanta)
    {
        Assert.Equal(konstanta, mode.ToDbValue());
        Assert.Equal(mode, CommissionModes.FromDbValue(konstanta));
    }

    [Theory]
    [InlineData(DisputeStatus.Open,     DisputeStatuses.Open)]
    [InlineData(DisputeStatus.Resolved, DisputeStatuses.Resolved)]
    public void Enum_dan_konstanta_status_sengketa_bolak_balik_konsisten(
        DisputeStatus status, string konstanta)
    {
        Assert.Equal(konstanta, status.ToDbValue());
        Assert.Equal(status, DisputeStatuses.FromDbValue(konstanta));
    }

    [Fact]
    public async Task Stempel_waktu_diisi_database()
    {
        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Role         = UserRole.Renter,
            Name         = "Uji Stempel",
            Email        = ApiFactory.UniqueEmail("stempel"),
            PasswordHash = hasher.Hash(ApiFactory.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, user.CreatedAt);
        Assert.NotEqual(default, user.UpdatedAt);
    }

    [Fact]
    public async Task Updated_at_dimajukan_trigger_saat_diubah()
    {
        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Role         = UserRole.Renter,
            Name         = "Sebelum",
            Email        = ApiFactory.UniqueEmail("trigger"),
            PasswordHash = hasher.Hash(ApiFactory.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        var sebelum = user.UpdatedAt;

        await Task.Delay(10);
        user.Name = "Sesudah";
        await db.SaveChangesAsync();

        Assert.True(user.UpdatedAt > sebelum,
            $"updated_at tidak maju: sebelum={sebelum:O} sesudah={user.UpdatedAt:O}");
    }

    [Fact]
    public async Task Setelan_platform_terbaca_dari_migrasi()
    {
        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);

        var settings = await db.PlatformSettings.AsNoTracking().SingleAsync();

        Assert.True(settings.Id);
        Assert.InRange(settings.CommissionRate, 0m, 1m);
        Assert.InRange(settings.ApprovalMinutes, 1, 1440);
        Assert.InRange(settings.PaymentMinutes, 1, 1440);
        Assert.InRange(settings.ReturnWindowDays, 1, 30);
        Assert.True(Enum.IsDefined(settings.CommissionMode));
    }

    [Theory]
    [InlineData(PriceUnit.Hour,  PriceUnits.Hour)]
    [InlineData(PriceUnit.Day,   PriceUnits.Day)]
    [InlineData(PriceUnit.Week,  PriceUnits.Week)]
    [InlineData(PriceUnit.Month, PriceUnits.Month)]
    public void Enum_dan_konstanta_satuan_harga_bolak_balik_konsisten(PriceUnit unit, string konstanta)
    {
        Assert.Equal(konstanta, unit.ToDbValue());
        Assert.Equal(unit, PriceUnits.FromDbValue(konstanta));
        Assert.Equal(konstanta, unit.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData(ItemStatus.Active,   ItemStatuses.Active)]
    [InlineData(ItemStatus.Inactive, ItemStatuses.Inactive)]
    public void Enum_dan_konstanta_status_item_bolak_balik_konsisten(ItemStatus status, string konstanta)
    {
        Assert.Equal(konstanta, status.ToDbValue());
        Assert.Equal(status, ItemStatuses.FromDbValue(konstanta));
    }

    [Theory]
    [InlineData(PriceUnit.Hour)]
    [InlineData(PriceUnit.Day)]
    [InlineData(PriceUnit.Week)]
    [InlineData(PriceUnit.Month)]
    public async Task Setiap_satuan_harga_diterima_database(PriceUnit unit)
    {
        var seller = await api.VerifiedSellerAsync();

        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);

        var item = new Item
        {
            SellerId  = seller.Id,
            Title     = $"Uji {unit}",
            Category  = "Uji",
            Price     = 1000m,
            PriceUnit = unit
        };

        db.Items.Add(item);
        await db.SaveChangesAsync();

        var tersimpan = await db.Items.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(unit, tersimpan.PriceUnit);
    }

    [Fact]
    public async Task Barang_milik_renter_ditolak_database()
    {
        var renter = await api.RegisterAsync(Roles.Renter);

        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);

        db.Items.Add(new Item
        {
            SellerId  = renter.User.Id,
            Title     = "Barang Renter",
            Category  = "Uji",
            Price     = 1000m,
            PriceUnit = PriceUnit.Day
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("seller", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Blackout_mengisi_starts_at_dan_ends_at_dari_range()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);

        var mulai = DateTime.UtcNow.AddDays(120).AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));
        var selesai = mulai.AddDays(2);

        var blackout = new ItemBlackout { ItemId = item.Id };
        db.ItemBlackouts.Add(blackout);
        db.SetDuring(blackout, mulai, selesai);

        await db.SaveChangesAsync();

        Assert.Equal(mulai, blackout.StartsAt, TimeSpan.FromSeconds(1));
        Assert.Equal(selesai, blackout.EndsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Status_di_view_kalender_sama_dengan_status_di_constraint_no_overlap()
    {
        using var scope = api.Services.CreateScope();
        var db = NewDb(scope);

        var constraintDef = await db.Database
            .SqlQuery<string>($"""
                SELECT pg_get_constraintdef(oid) AS "Value"
                FROM pg_constraint WHERE conname = 'no_overlap'
                """)
            .SingleAsync();

        var viewDef = await db.Database
            .SqlQuery<string>($"""
                SELECT pg_get_viewdef('item_blocked_ranges'::regclass) AS "Value"
                """)
            .SingleAsync();

        foreach (var status in (string[])["pending", "confirmed", "active"])
        {
            Assert.Contains($"'{status}'", constraintDef);
            Assert.Contains($"'{status}'", viewDef);
        }

        foreach (var status in (string[])["completed", "cancelled", "disputed"])
        {
            Assert.DoesNotContain($"'{status}'", constraintDef);
            Assert.DoesNotContain($"'{status}'", viewDef);
        }
    }
}
