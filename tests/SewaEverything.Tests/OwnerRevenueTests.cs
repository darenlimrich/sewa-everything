using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class OwnerRevenueTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1400 + n).AddHours(9);

    private async Task<RevenueSummaryResponse> RingkasanAsync()
    {
        var owner = await api.ClientAsOwnerAsync();
        return (await owner.GetFromJsonAsync<RevenueSummaryResponse>("/owner/revenue"))!;
    }

    private static void PastikanSeimbang(RevenueSummaryResponse r) =>
        Assert.Equal(r.CashHeld, r.PayoutsDue + r.CommissionEarned + r.InEscrow);

    [Fact]
    public async Task Revenue_tertutup_untuk_admin_dan_renter()
    {
        var admin = await api.ClientAsAdminAsync();
        var renter = await api.ClientAsAsync(Roles.Renter);

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/owner/revenue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/owner/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await renter.GetAsync("/owner/revenue")).StatusCode);
    }

    [Fact]
    public async Task Uang_bergerak_dari_escrow_ke_komisi_lalu_keluar_platform()
    {
        var awal = await RingkasanAsync();
        PastikanSeimbang(awal);

        var s = await api.ActiveBookingAsync(Slot(1), Slot(3), price: 100_000m, deposit: 500_000m);

        var fee     = 10_000m;
        var masuk   = 700_000m;
        var keluar  = masuk - fee;

        Assert.Equal(fee, s.Booking.PlatformFeeAmount);

        var dibayar = await RingkasanAsync();
        PastikanSeimbang(dibayar);

        Assert.Equal(masuk, dibayar.MoneyIn - awal.MoneyIn);
        Assert.Equal(masuk, dibayar.CashHeld - awal.CashHeld);

        Assert.Equal(masuk, dibayar.InEscrow - awal.InEscrow);
        Assert.Equal(0m, dibayar.CommissionEarned - awal.CommissionEarned);
        Assert.Equal(0m, dibayar.PayoutsDue - awal.PayoutsDue);

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        var selesai = await RingkasanAsync();
        PastikanSeimbang(selesai);

        Assert.Equal(masuk, selesai.CashHeld - awal.CashHeld);
        Assert.Equal(0m, selesai.MoneyOut - awal.MoneyOut);

        Assert.Equal(fee, selesai.CommissionEarned - awal.CommissionEarned);
        Assert.Equal(fee, selesai.CommissionLast30Days - awal.CommissionLast30Days);
        Assert.Equal(keluar, selesai.PayoutsDue - awal.PayoutsDue);
        Assert.Equal(2, selesai.PendingPayoutCount - awal.PendingPayoutCount);

        Assert.Equal(0m, selesai.InEscrow - awal.InEscrow);
        Assert.Equal(1, selesai.CompletedBookings - awal.CompletedBookings);

        var admin = await api.ClientAsAdminAsync();
        var antrean = await admin.GetFromJsonAsync<List<PendingPayoutResponse>>("/admin/payouts/pending");

        foreach (var kewajiban in antrean!.Where(p => p.BookingId == s.Booking.Id))
        {
            (await admin.PostAsync($"/admin/payouts/{kewajiban.Id}/settle", null))
                .EnsureSuccessStatusCode();
        }

        var terkirim = await RingkasanAsync();
        PastikanSeimbang(terkirim);

        Assert.Equal(keluar, terkirim.MoneyOut - awal.MoneyOut);
        Assert.Equal(0m, terkirim.PayoutsDue - awal.PayoutsDue);

        Assert.Equal(fee, terkirim.CashHeld - awal.CashHeld);
        Assert.Equal(fee, terkirim.CommissionEarned - awal.CommissionEarned);
        Assert.Equal(0m, terkirim.InEscrow - awal.InEscrow);
    }

    [Fact]
    public async Task Pembatalan_tidak_menghasilkan_komisi()
    {
        var awal = await RingkasanAsync();

        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(11), Slot(13));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        await api.SettleAsync(renter.Client, booking.Id);

        (await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/cancel",
            new CancelBookingRequest { Reason = "Berubah pikiran" })).EnsureSuccessStatusCode();

        var sesudah = await RingkasanAsync();
        PastikanSeimbang(sesudah);

        Assert.Equal(0m, sesudah.CommissionEarned - awal.CommissionEarned);
        Assert.Equal(700_000m, sesudah.MoneyIn - awal.MoneyIn);
        Assert.Equal(700_000m, sesudah.PayoutsDue - awal.PayoutsDue);
        Assert.Equal(0m, sesudah.InEscrow - awal.InEscrow);
    }

    [Fact]
    public async Task Transaksi_menampilkan_baris_buku_besar_lengkap_dengan_nama_barang()
    {
        var s = await api.ActiveBookingAsync(Slot(21), Slot(23), price: 100_000m, deposit: 500_000m);
        var owner = await api.ClientAsOwnerAsync();

        var halaman = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            "/owner/transactions?pageSize=100");

        var punyaKami = halaman!.Items.Where(t => t.BookingId == s.Booking.Id).ToList();

        Assert.Equal(2, punyaKami.Count);
        Assert.All(punyaKami, t => Assert.Equal("Kamera Mirrorless", t.ItemTitle));
        Assert.All(punyaKami, t => Assert.Equal(PaymentStatuses.Paid, t.Status));
        Assert.Contains(punyaKami, t => t.Kind == PaymentKinds.RentCharge && t.Amount == 200_000m);
        Assert.Contains(punyaKami, t => t.Kind == PaymentKinds.DepositCharge && t.Amount == 500_000m);

        Assert.All(punyaKami, t => Assert.False(string.IsNullOrWhiteSpace(t.CounterpartyName)));

        var waktu = halaman.Items.Select(t => t.CreatedAt).ToList();
        Assert.Equal(waktu.OrderByDescending(w => w), waktu);
    }

    [Fact]
    public async Task Transaksi_bisa_disaring_per_jenis_dan_status()
    {
        var s = await api.ActiveBookingAsync(Slot(31), Slot(33), price: 100_000m, deposit: 500_000m);

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        var owner = await api.ClientAsOwnerAsync();

        var komisi = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            $"/owner/transactions?kind={PaymentKinds.PlatformFee}&pageSize=100");

        Assert.All(komisi!.Items, t => Assert.Equal(PaymentKinds.PlatformFee, t.Kind));
        Assert.Contains(komisi.Items, t => t.BookingId == s.Booking.Id && t.Amount == 10_000m);

        var tertunda = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            $"/owner/transactions?status={PaymentStatuses.Pending}&pageSize=100");

        Assert.All(tertunda!.Items, t => Assert.Equal(PaymentStatuses.Pending, t.Status));
        Assert.Contains(tertunda.Items, t => t.BookingId == s.Booking.Id
                                             && t.Kind == PaymentKinds.SellerPayout);
    }

    [Fact]
    public async Task Penyaring_yang_tidak_dikenal_ditolak()
    {
        var owner = await api.ClientAsOwnerAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.GetAsync("/owner/transactions?kind=uang_jajan")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.GetAsync("/owner/transactions?status=entahlah")).StatusCode);
    }

    [Fact]
    public async Task Paginasi_menghormati_ukuran_halaman()
    {
        await api.ActiveBookingAsync(Slot(41), Slot(43));

        var owner = await api.ClientAsOwnerAsync();

        var halaman = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            "/owner/transactions?page=1&pageSize=2");

        Assert.Equal(2, halaman!.Items.Count);
        Assert.Equal(1, halaman.Page);
        Assert.True(halaman.Total >= 2);

        var kedua = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            "/owner/transactions?page=2&pageSize=2");

        Assert.Empty(halaman.Items.Select(t => t.Id).Intersect(kedua!.Items.Select(t => t.Id)));
    }

    [Fact]
    public async Task Halaman_di_luar_jangkauan_menjawab_daftar_kosong_bukan_error()
    {
        var owner = await api.ClientAsOwnerAsync();

        var halaman = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            "/owner/transactions?page=9999&pageSize=20");

        Assert.Empty(halaman!.Items);
    }
}
