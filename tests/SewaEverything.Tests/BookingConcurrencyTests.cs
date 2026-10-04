using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class BookingConcurrencyTests(ApiFactory api)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Dua_request_paralel_hanya_satu_yang_berhasil(int iterasi)
    {
        var seller = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();

        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
            .AddDays(120 + iterasi).AddHours(9);
        var selesai = mulai.AddDays(3);

        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = Task.Run(async () =>
        {
            await gerbang.Task;
            return await api.BookAsync(renterA.Client, item.Id, mulai, selesai);
        });

        var b = Task.Run(async () =>
        {
            await gerbang.Task;
            return await api.BookAsync(renterB.Client, item.Id, mulai, selesai);
        });

        gerbang.SetResult();

        var hasil = await Task.WhenAll(a, b);

        var berhasil = hasil.Count(r => r.StatusCode == HttpStatusCode.Created);
        var ditolak  = hasil.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, berhasil);
        Assert.Equal(1, ditolak);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var tersimpan = await db.Bookings.AsNoTracking()
            .CountAsync(x => x.ItemId == item.Id);

        Assert.Equal(1, tersimpan);
    }

    [Fact]
    public async Task Lima_request_paralel_tetap_hanya_satu_yang_berhasil()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var renters = new List<ApiFactory.UserContext>();
        for (var i = 0; i < 5; i++)
        {
            renters.Add(await api.RenterAsync());
        }

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(200).AddHours(9);
        var selesai = mulai.AddDays(2);

        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tugas = renters.Select(r => Task.Run(async () =>
        {
            await gerbang.Task;
            return await api.BookAsync(r.Client, item.Id, mulai, selesai);
        })).ToArray();

        gerbang.SetResult();

        var hasil = await Task.WhenAll(tugas);

        Assert.Equal(1, hasil.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, hasil.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        Assert.Equal(1, await db.Bookings.AsNoTracking().CountAsync(x => x.ItemId == item.Id));
    }

    [Fact]
    public async Task Penolakan_datang_dari_constraint_bukan_dari_pemeriksaan_csharp()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var penahan = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(300).AddHours(9);
        var selesai = mulai.AddDays(2);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO bookings (
                item_id, renter_id, during, status,
                price_snapshot, price_unit_snapshot, duration_units,
                total_rent, deposit_amount,
                platform_fee_rate, platform_fee_mode, platform_fee_amount, hold_expires_at)
            VALUES (
                {item.Id}, {penahan.Id},
                tstzrange({mulai.UtcDateTime}, {selesai.UtcDateTime}, '[)'), 'pending',
                100000, 'day', 2, 200000, 0, 0, 'deduct', 0, now() + interval '15 minutes')
            """);

        var request = api.BookAsync(renter.Client, item.Id, mulai, selesai);

        await Task.Delay(500);
        Assert.False(request.IsCompleted, "request seharusnya masih terblokir menunggu transaksi penahan");

        await tx.CommitAsync();

        var response = await request.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Contains("baru saja diambil", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProblemShape(string? Title, string? Detail);

    [Fact]
    public async Task Rentang_berbeda_untuk_barang_sama_lolos_semua()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var renters = new List<ApiFactory.UserContext>();
        for (var i = 0; i < 4; i++)
        {
            renters.Add(await api.RenterAsync());
        }

        var awal = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(250).AddHours(9);
        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tugas = renters.Select((r, i) => Task.Run(async () =>
        {
            await gerbang.Task;

            return await api.BookAsync(r.Client, item.Id, awal.AddDays(i * 2), awal.AddDays(i * 2 + 2));
        })).ToArray();

        gerbang.SetResult();

        var hasil = await Task.WhenAll(tugas);

        Assert.All(hasil, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }
}
