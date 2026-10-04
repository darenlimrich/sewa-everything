using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class DisputeTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1400 + n).AddHours(9);

    private static PaymentResponse Row(IReadOnlyList<PaymentResponse> entries, string kind) =>
        entries.Single(e => e.Kind == kind);

    private async Task<(ApiFactory.ActiveScenario S, Guid DisputeId, HttpClient Admin)> DisputedAsync(
        int slot, decimal deposit = 500_000m)
    {
        var s = await api.ActiveBookingAsync(
            Slot(slot), Slot(slot + 2), deposit: deposit, channel: PaymentChannels.VaBca);

        (await s.Seller.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "Barang kembali dalam kondisi rusak." }))
            .EnsureSuccessStatusCode();

        var admin = await api.ClientAsAdminAsync();
        var disputes = await admin.GetFromJsonAsync<List<DisputeResponse>>("/admin/disputes");

        return (s, disputes!.Single(d => d.BookingId == s.Booking.Id).Id, admin);
    }

    [Fact]
    public async Task Seller_mengajukan_sengketa_memindahkan_ke_disputed()
    {
        var s = await api.ActiveBookingAsync(Slot(1), Slot(3));

        var response = await s.Seller.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "Barang kembali dengan lecet besar." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Disputed, await api.StatusOfAsync(s.Booking.Id));

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);
        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.SellerPayout);
        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.DepositRefund);
    }

    [Fact]
    public async Task Renter_juga_bisa_mengajukan_sengketa()
    {
        var s = await api.ActiveBookingAsync(Slot(11), Slot(13));

        var response = await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "Deposit saya terancam dipotong sepihak." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Disputed, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Sengketa_alasan_kosong_ditolak()
    {
        var s = await api.ActiveBookingAsync(Slot(21), Slot(23));

        var response = await s.Seller.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Admin_tidak_bisa_mengajukan_sengketa()
    {
        var s = await api.ActiveBookingAsync(Slot(31), Slot(33));
        var admin = await api.ClientAsAdminAsync();

        var response = await admin.PostAsJsonAsync($"/bookings/{s.Booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "admin mencoba" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Sengketa_hanya_dari_booking_active()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(41), Slot(43));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var response = await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "belum active" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_melihat_sengketa_terbuka_dengan_deposit_tertagih()
    {
        var (s, disputeId, admin) = await DisputedAsync(51, deposit: 500_000m);

        var disputes = await admin.GetFromJsonAsync<List<DisputeResponse>>("/admin/disputes");
        var d = disputes!.Single(x => x.Id == disputeId);

        Assert.Equal(DisputeStatuses.Open, d.Status);
        Assert.Equal(s.Booking.Id, d.BookingId);
        Assert.Equal(500_000m, d.DepositCollected);
    }

    [Fact]
    public async Task Admin_memutus_sengketa_dengan_potongan_membagi_deposit()
    {
        var (s, disputeId, admin) = await DisputedAsync(61, deposit: 500_000m);

        var response = await admin.PostAsJsonAsync($"/admin/disputes/{disputeId}/resolve",
            new ResolveDisputeRequest
            {
                DepositDeduction = 150_000m,
                Resolution       = "Lecet di beberapa sisi; potong Rp 150.000."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Completed, await api.StatusOfAsync(s.Booking.Id));

        var dispute = (await response.Content.ReadFromJsonAsync<DisputeResponse>())!;
        Assert.Equal(DisputeStatuses.Resolved, dispute.Status);

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        var refund = Row(entries, PaymentKinds.DepositRefund);
        Assert.Equal(350_000m, refund.Amount);
        Assert.Equal(PaymentMethods.Disbursement, refund.Method);

        var forfeit = Row(entries, PaymentKinds.DepositForfeit);
        Assert.Equal(150_000m, forfeit.Amount);
        Assert.Equal(PaymentDirections.Internal, forfeit.Direction);

        Assert.Equal(340_000m, Row(entries, PaymentKinds.SellerPayout).Amount);
        Assert.Equal(10_000m,  Row(entries, PaymentKinds.PlatformFee).Amount);

        Assert.Equal(500_000m, refund.Amount + forfeit.Amount);
        var masuk  = entries.Where(e => e.Direction == PaymentDirections.In).Sum(e => e.Amount);
        var keluar = entries.Where(e => e.Direction == PaymentDirections.Out).Sum(e => e.Amount);
        Assert.Equal(10_000m, masuk - keluar);
    }

    [Fact]
    public async Task Potongan_melebihi_deposit_tertagih_ditolak()
    {
        var (s, disputeId, admin) = await DisputedAsync(71, deposit: 500_000m);

        var response = await admin.PostAsJsonAsync($"/admin/disputes/{disputeId}/resolve",
            new ResolveDisputeRequest { DepositDeduction = 600_000m, Resolution = "kebanyakan" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(BookingStatuses.Disputed, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Resolusi_tanpa_catatan_ditolak()
    {
        var (_, disputeId, admin) = await DisputedAsync(81);

        var response = await admin.PostAsJsonAsync($"/admin/disputes/{disputeId}/resolve",
            new ResolveDisputeRequest { DepositDeduction = 0m, Resolution = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sengketa_yang_sudah_diputus_tidak_bisa_diputus_ulang()
    {
        var (_, disputeId, admin) = await DisputedAsync(91);

        (await admin.PostAsJsonAsync($"/admin/disputes/{disputeId}/resolve",
            new ResolveDisputeRequest { DepositDeduction = 0m, Resolution = "Beres, tidak ada potongan." }))
            .EnsureSuccessStatusCode();

        var kedua = await admin.PostAsJsonAsync($"/admin/disputes/{disputeId}/resolve",
            new ResolveDisputeRequest { DepositDeduction = 0m, Resolution = "coba lagi" });

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Fact]
    public async Task Non_admin_tidak_bisa_melihat_daftar_sengketa()
    {
        var renter = await api.RenterAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await renter.Client.GetAsync("/admin/disputes")).StatusCode);
    }
}
