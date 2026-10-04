using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class AdminPayoutTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1800 + n).AddHours(9);

    private async Task<(Guid BookingId, HttpClient Admin)> CompletedAsync(int slot)
    {
        var s = await api.ActiveBookingAsync(Slot(slot), Slot(slot + 2));

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        return (s.Booking.Id, await api.ClientAsAdminAsync());
    }

    private static Task<List<PendingPayoutResponse>?> PendingAsync(HttpClient admin) =>
        admin.GetFromJsonAsync<List<PendingPayoutResponse>>("/admin/payouts/pending");

    [Fact]
    public async Task Pencairan_tertunda_muncul_setelah_sewa_selesai()
    {
        var (bookingId, admin) = await CompletedAsync(1);

        var mine = (await PendingAsync(admin))!.Where(p => p.BookingId == bookingId).ToList();

        Assert.Contains(mine, p => p.Kind == PaymentKinds.SellerPayout && p.Amount == 190_000m);
        Assert.Contains(mine, p => p.Kind == PaymentKinds.DepositRefund && p.Amount == 500_000m);
    }

    [Fact]
    public async Task Menyelesaikan_pencairan_mengeluarkannya_dari_antrean()
    {
        var (bookingId, admin) = await CompletedAsync(11);

        var payout = (await PendingAsync(admin))!
            .First(p => p.BookingId == bookingId && p.Kind == PaymentKinds.SellerPayout);

        var response = await admin.PostAsync($"/admin/payouts/{payout.Id}/settle", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.DoesNotContain((await PendingAsync(admin))!, p => p.Id == payout.Id);
    }

    [Fact]
    public async Task Pencairan_yang_sudah_diproses_tidak_bisa_diproses_ulang()
    {
        var (bookingId, admin) = await CompletedAsync(21);

        var payout = (await PendingAsync(admin))!
            .First(p => p.BookingId == bookingId && p.Kind == PaymentKinds.SellerPayout);

        (await admin.PostAsync($"/admin/payouts/{payout.Id}/settle", null)).EnsureSuccessStatusCode();
        var kedua = await admin.PostAsync($"/admin/payouts/{payout.Id}/settle", null);

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Fact]
    public async Task Settle_yang_bukan_pencairan_keluar_404()
    {
        var (bookingId, admin) = await CompletedAsync(31);

        var ledger = await admin.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{bookingId}/ledger");
        var rentCharge = ledger!.Entries.First(e => e.Kind == PaymentKinds.RentCharge);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsync($"/admin/payouts/{rentCharge.Id}/settle", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsync($"/admin/payouts/{Guid.NewGuid()}/settle", null)).StatusCode);
    }

    [Fact]
    public async Task Non_admin_tidak_bisa_melihat_antrean_pencairan()
    {
        var seller = await api.VerifiedSellerAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await seller.Client.GetAsync("/admin/payouts/pending")).StatusCode);
    }
}
