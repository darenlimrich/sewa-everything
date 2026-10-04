using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class EscrowTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int hariKe) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(900 + hariKe).AddHours(9);

    private sealed record Skenario(
        ApiFactory.UserContext Seller, ApiFactory.UserContext Renter, BookingResponse Booking);

    private async Task<Skenario> ConfirmedAsync(int hariKe)
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(hariKe), Slot(hariKe + 2));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        return new Skenario(seller, renter, booking);
    }

    [Fact]
    public async Task Serah_terima_ditolak_kalau_belum_dibayar_sama_sekali()
    {
        var s = await ConfirmedAsync(1);

        var response = await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/handover", null);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal(BookingStatuses.Confirmed, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Serah_terima_ditolak_kalau_tagihan_masih_menunggu()
    {
        var s = await ConfirmedAsync(5);
        await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);

        var response = await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/handover", null);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal(BookingStatuses.Confirmed, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Serah_terima_diterima_setelah_uang_benar_benar_masuk()
    {
        var s = await ConfirmedAsync(10);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        var response = await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/handover", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(s.Booking.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pembatalan_setelah_dibayar_mencatat_kewajiban_refund_penuh(bool olehRenter)
    {
        var s = await ConfirmedAsync(olehRenter ? 15 : 20);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        var client = olehRenter ? s.Renter.Client : s.Seller.Client;

        var response = await client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/cancel",
            new CancelBookingRequest { Reason = "Berubah rencana" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(700_000m, ledger!.AmountSettled);
        Assert.Equal(700_000m, ledger.AmountRefundPending);

        var sewa = ledger.Entries.Single(e => e.Kind == PaymentKinds.RentRefund);
        var deposit = ledger.Entries.Single(e => e.Kind == PaymentKinds.DepositRefund);

        Assert.Equal(200_000m, sewa.Amount);
        Assert.Equal(500_000m, deposit.Amount);
        Assert.All(new[] { sewa, deposit }, e =>
        {
            Assert.Equal(PaymentDirections.Out, e.Direction);

            Assert.Equal(PaymentStatuses.Pending, e.Status);
        });
    }

    [Fact]
    public async Task Pembatalan_sebelum_dibayar_tidak_mencatat_refund()
    {
        var s = await ConfirmedAsync(25);

        await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/cancel",
            new CancelBookingRequest());

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(0m, ledger!.AmountRefundPending);
        Assert.Empty(ledger.Entries);
    }

    [Theory]
    [InlineData(PaymentChannels.Gopay, PaymentMethods.GatewayReversal)]
    [InlineData(PaymentChannels.VaBca, PaymentMethods.Disbursement)]
    public async Task Metode_refund_mengikuti_cara_bayarnya(string channel, string metode)
    {
        var s = await ConfirmedAsync(30 + channel.Length);

        await api.AddPayoutAccountAsync(s.Renter.Client);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id, channel);

        await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/cancel",
            new CancelBookingRequest());

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        var refunds = ledger!.Entries.Where(e => e.Direction == PaymentDirections.Out).ToList();

        Assert.NotEmpty(refunds);
        Assert.All(refunds, e => Assert.Equal(metode, e.Method));
    }

    [Fact]
    public async Task Pembatalan_yang_ditolak_tidak_mencatat_refund()
    {
        var s = await ConfirmedAsync(45);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/handover", null))
            .EnsureSuccessStatusCode();
        await api.DriveToStatusAsync(s.Booking.Id, BookingStatuses.Completed);

        var response = await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/cancel",
            new CancelBookingRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(0m, ledger!.AmountRefundPending);
    }

    [Fact]
    public async Task Pembatalan_dipanggil_dua_kali_tidak_melipatgandakan_kewajiban()
    {
        var s = await ConfirmedAsync(50);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/cancel",
            new CancelBookingRequest());

        var kedua = await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/cancel",
            new CancelBookingRequest());
        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(700_000m, ledger!.AmountRefundPending);
        Assert.Equal(2, ledger.Entries.Count(e => e.Direction == PaymentDirections.Out));
    }
}
