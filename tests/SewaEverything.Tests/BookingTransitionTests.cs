using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class BookingTransitionTests(ApiFactory api)
{
    private static readonly string[] SemuaStatus =
    [
        BookingStatuses.Pending, BookingStatuses.Confirmed, BookingStatuses.Active,
        BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.Disputed
    ];

    private int _hariKe = 300;

    private async Task<Guid> BookingBaruAsync()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
            .AddDays(Interlocked.Add(ref _hariKe, 5)).AddHours(9);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));
        return booking.Id;
    }

    [Fact]
    public async Task Trigger_database_dan_tabel_transisi_csharp_sepakat_untuk_semua_pasangan()
    {
        var selisih = new List<string>();

        foreach (var dari in SemuaStatus)
        {
            foreach (var ke in SemuaStatus)
            {
                if (dari == ke)
                {
                    continue;
                }

                var bookingId = await BookingBaruAsync();
                await api.DriveToStatusAsync(bookingId, dari);

                var diterimaDb = await CobaUbahStatusAsync(bookingId, ke);
                var diizinkanCs = BookingTransitions.IsAllowed(
                    BookingStatuses.FromDbValue(dari), BookingStatuses.FromDbValue(ke));

                if (diterimaDb != diizinkanCs)
                {
                    selisih.Add($"{dari} -> {ke}: database={(diterimaDb ? "terima" : "tolak")}, " +
                                $"C#={(diizinkanCs ? "izinkan" : "larang")}");
                }

                if (!diterimaDb)
                {
                    Assert.Equal(dari, await api.StatusOfAsync(bookingId));
                }
            }
        }

        Assert.True(selisih.Count == 0,
            "Trigger database dan BookingTransitions tidak sepakat:\n  " + string.Join("\n  ", selisih));
    }

    [Fact]
    public async Task Menyetel_status_ke_nilainya_sendiri_bukan_transisi()
    {
        var bookingId = await BookingBaruAsync();
        await api.DriveToStatusAsync(bookingId, BookingStatuses.Active);

        Assert.True(await CobaUbahStatusAsync(bookingId, BookingStatuses.Active));
        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(bookingId));
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    public void Status_final_tidak_punya_transisi_lanjutan(string status)
    {
        Assert.Empty(BookingTransitions.AllowedFrom(BookingStatuses.FromDbValue(status)));
    }

    [Fact]
    public async Task Approve_pada_booking_bukan_pending_ditolak_409()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(400).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        await api.DriveToStatusAsync(booking.Id, BookingStatuses.Active);

        var response = await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Handover_pada_booking_masih_pending_ditolak_409()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(405).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        var response = await seller.Client.PostAsync($"/bookings/{booking.Id}/handover", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Membatalkan_booking_yang_sudah_selesai_ditolak_409()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(410).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        await api.DriveToStatusAsync(booking.Id, BookingStatuses.Completed);

        var response = await renter.Client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/cancel", new CancelBookingRequest { Reason = "Berubah pikiran" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookingStatuses.Completed, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Alur_lengkap_pending_confirmed_active()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(415).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        var disetujui = await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);
        disetujui.EnsureSuccessStatusCode();
        var setelahApprove = (await disetujui.Content.ReadFromJsonAsync<BookingResponse>())!;

        Assert.Equal(BookingStatuses.Confirmed, setelahApprove.Status);

        Assert.NotNull(setelahApprove.HoldExpiresAt);
        Assert.True(setelahApprove.HoldExpiresAt < booking.HoldExpiresAt,
            "setelah disetujui, tenggat harus berganti jadi jendela pembayaran yang lebih pendek");

        await api.SettleAsync(renter.Client, booking.Id);

        var diserahkan = await seller.Client.PostAsync($"/bookings/{booking.Id}/handover", null);
        diserahkan.EnsureSuccessStatusCode();
        var setelahHandover = (await diserahkan.Content.ReadFromJsonAsync<BookingResponse>())!;

        Assert.Equal(BookingStatuses.Active, setelahHandover.Status);
        Assert.Null(setelahHandover.HoldExpiresAt);
    }

    [Fact]
    public async Task Setiap_perpindahan_status_meninggalkan_jejak_audit()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(420).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);
        await api.SettleAsync(renter.Client, booking.Id);
        await seller.Client.PostAsync($"/bookings/{booking.Id}/handover", null);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var riwayat = await db.Database
            .SqlQuery<string>($"""
                SELECT to_status AS "Value" FROM booking_status_history
                WHERE booking_id = {booking.Id} ORDER BY created_at, id
                """)
            .ToListAsync();

        Assert.Equal(
            [BookingStatuses.Pending, BookingStatuses.Confirmed, BookingStatuses.Active],
            riwayat);
    }

    private async Task<bool> CobaUbahStatusAsync(Guid bookingId, string status)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET status = {status} WHERE id = {bookingId}");
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return false;
        }
    }
}
