using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReturnCompletionTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1200 + n).AddHours(9);

    private static PaymentResponse Row(IReadOnlyList<PaymentResponse> entries, string kind) =>
        entries.Single(e => e.Kind == kind);

    [Fact]
    public async Task Return_menutup_booking_dan_mengembalikan_deposit_penuh()
    {
        var s = await api.ActiveBookingAsync(Slot(1), Slot(3), price: 100_000m, deposit: 500_000m);

        var response = await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Completed, await api.StatusOfAsync(s.Booking.Id));

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        var refund = Row(entries, PaymentKinds.DepositRefund);
        Assert.Equal(500_000m, refund.Amount);
        Assert.Equal(PaymentMethods.GatewayReversal, refund.Method);
        Assert.Equal(PaymentStatuses.Pending, refund.Status);

        var payout = Row(entries, PaymentKinds.SellerPayout);
        Assert.Equal(190_000m, payout.Amount);
        Assert.Equal(PaymentMethods.Disbursement, payout.Method);
        Assert.Equal(PaymentStatuses.Pending, payout.Status);

        var fee = Row(entries, PaymentKinds.PlatformFee);
        Assert.Equal(10_000m, fee.Amount);
        Assert.Equal(PaymentDirections.Internal, fee.Direction);
        Assert.Equal(PaymentStatuses.Paid, fee.Status);

        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.DepositForfeit);

        var masuk  = entries.Where(e => e.Direction == PaymentDirections.In).Sum(e => e.Amount);
        var keluar = entries.Where(e => e.Direction == PaymentDirections.Out).Sum(e => e.Amount);
        Assert.Equal(10_000m, masuk - keluar);
    }

    [Fact]
    public async Task Ledger_refund_pending_tidak_ikut_menghitung_pencairan_seller()
    {
        var s = await api.ActiveBookingAsync(Slot(61), Slot(63), price: 100_000m, deposit: 500_000m);

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        var keluarTertunda = ledger!.Entries
            .Where(e => e.Direction == PaymentDirections.Out && e.Status == PaymentStatuses.Pending)
            .Sum(e => e.Amount);

        Assert.Equal(690_000m, keluarTertunda);
        Assert.Equal(500_000m, ledger.AmountRefundPending);
    }

    [Fact]
    public async Task Return_mode_on_top_membagi_dana_dengan_benar()
    {
        await WithCommissionAsync(0.10m, CommissionModes.OnTop, async () =>
        {
            var s = await api.ActiveBookingAsync(Slot(11), Slot(13), price: 100_000m, deposit: 50_000m);

            (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
                .EnsureSuccessStatusCode();

            var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

            Assert.Equal(200_000m, Row(entries, PaymentKinds.SellerPayout).Amount);
            Assert.Equal(50_000m,  Row(entries, PaymentKinds.DepositRefund).Amount);
            Assert.Equal(20_000m,  Row(entries, PaymentKinds.PlatformFee).Amount);

            var masuk  = entries.Where(e => e.Direction == PaymentDirections.In).Sum(e => e.Amount);
            var keluar = entries.Where(e => e.Direction == PaymentDirections.Out).Sum(e => e.Amount);
            Assert.Equal(20_000m, masuk - keluar);
        });
    }

    [Fact]
    public async Task Return_hanya_boleh_pemilik_barang()
    {
        var s = await api.ActiveBookingAsync(Slot(21), Slot(23));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Renter.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null)).StatusCode);

        var lain = await api.VerifiedSellerAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await lain.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null)).StatusCode);

        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Return_ditolak_kalau_belum_active()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(31), Slot(33));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var response = await seller.Client.PostAsync($"/bookings/{booking.Id}/return", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookingStatuses.Confirmed, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Return_kedua_kali_ditolak_dan_tidak_melipat_dana()
    {
        var s = await api.ActiveBookingAsync(Slot(41), Slot(43));

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        var kedua = await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null);
        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);
        Assert.Single(entries, e => e.Kind == PaymentKinds.SellerPayout);
        Assert.Single(entries, e => e.Kind == PaymentKinds.DepositRefund);
        Assert.Single(entries, e => e.Kind == PaymentKinds.PlatformFee);
    }

    [Fact]
    public async Task Return_tanpa_deposit_tidak_membuat_baris_refund_deposit()
    {
        var s = await api.ActiveBookingAsync(Slot(51), Slot(53), price: 100_000m, deposit: 0m);

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.DepositRefund);
        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.DepositForfeit);
        Assert.Equal(190_000m, Row(entries, PaymentKinds.SellerPayout).Amount);
        Assert.Equal(10_000m,  Row(entries, PaymentKinds.PlatformFee).Amount);
    }

    private async Task WithCommissionAsync(decimal rate, string mode, Func<Task> body)
    {
        var owner = await api.ClientAsOwnerAsync();

        async Task SetAsync(decimal r, string m) =>
            (await owner.PutAsJsonAsync("/owner/settings", new UpdatePlatformSettingsRequest
            {
                CommissionRate   = r,
                CommissionMode   = m,
                ApprovalMinutes  = 1440,
                PaymentMinutes   = 60,
                ReturnWindowDays = 3
            })).EnsureSuccessStatusCode();

        await SetAsync(rate, mode);

        try
        {
            await body();
        }
        finally
        {
            await SetAsync(0.05m, CommissionModes.Deduct);
        }
    }
}
