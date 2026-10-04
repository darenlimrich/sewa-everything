using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class PulseTests(ApiFactory api)
{
    private static readonly DateTimeOffset Mulai = DateTimeOffset.UtcNow.Date.AddDays(5);
    private static readonly DateTimeOffset Selesai = Mulai.AddDays(2);

    [Fact]
    public async Task Akun_baru_denyutnya_kosong()
    {
        var renter = await api.RenterAsync();

        var pulse = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Null(pulse!.BookingsStamp);
        Assert.Equal(0, pulse.Unread);
        Assert.Equal(0, pulse.ActionNeeded);
        Assert.Equal(0, pulse.CartCount);
    }

    [Fact]
    public async Task Stempel_bergerak_saat_sewa_berubah_status()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        var sebelum = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.NotNull(sebelum!.BookingsStamp);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var sesudah = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.True(sesudah!.BookingsStamp > sebelum.BookingsStamp);
        Assert.NotEqual(sebelum, sesudah);
    }

    [Fact]
    public async Task Penyewa_yang_sewanya_disetujui_punya_satu_tindakan()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        var sebelum = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(0, sebelum!.ActionNeeded);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var sesudah = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(1, sesudah!.ActionNeeded);
    }

    [Fact]
    public async Task Pemilik_barang_punya_tindakan_saat_ada_permintaan_masuk()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        var sebelum = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        var awal = sebelum!.ActionNeeded;

        await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        var sesudah = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(awal + 1, sesudah!.ActionNeeded);
    }

    [Fact]
    public async Task Belum_terbaca_ikut_turun_setelah_ditandai()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        var sebelum = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(1, sebelum!.Unread);

        (await renter.Client.PostAsync("/notifications/seen", null)).EnsureSuccessStatusCode();

        var sesudah = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(0, sesudah!.Unread);
    }

    [Fact]
    public async Task Keranjang_ikut_terhitung()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        (await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId  = item.Id,
            StartAt = Mulai.UtcDateTime,
            EndAt   = Selesai.UtcDateTime
        })).EnsureSuccessStatusCode();

        var pulse = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Equal(1, pulse!.CartCount);
    }

    [Fact]
    public async Task Denyut_orang_lain_tidak_bocor()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renterA.Client, item.Id, Mulai, Selesai);

        var punyaB = await renterB.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Null(punyaB!.BookingsStamp);
        Assert.Equal(0, punyaB.Unread);
        Assert.Equal(0, punyaB.ActionNeeded);
    }

    [Fact]
    public async Task Penyewa_tidak_melihat_angka_antrean_staf()
    {
        var renter = await api.RenterAsync();

        var pulse = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Equal(0, pulse!.OpenDisputes);
        Assert.Equal(0, pulse.PendingPayouts);
        Assert.Equal(0, pulse.PendingSellers);
        Assert.Equal(0, pulse.SuspendedItems);
    }

    [Fact]
    public async Task Admin_melihat_antrean_pemilik_yang_menunggu_verifikasi()
    {
        var admin = await api.ClientAsAdminAsync();

        var sebelum = await admin.GetFromJsonAsync<PulseResponse>("/pulse");

        await api.SellerAsync();

        var sesudah = await admin.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Equal(sebelum!.PendingSellers + 1, sesudah!.PendingSellers);
    }

    [Fact]
    public async Task Owner_bukan_admin_jadi_tidak_membawa_antrean()
    {
        var owner = await api.ClientAsOwnerAsync();

        var pulse = await owner.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Equal(0, pulse!.OpenDisputes);
        Assert.Equal(0, pulse.PendingPayouts);
        Assert.Equal(0, pulse.PendingSellers);
    }

    [Fact]
    public async Task Stempel_barang_bergerak_saat_admin_menurunkan_listing()
    {
        var seller = await api.VerifiedSellerAsync();
        var admin  = await api.ClientAsAdminAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        var sebelum = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.NotNull(sebelum!.ItemsStamp);

        (await admin.PostAsJsonAsync($"/admin/items/{item.Id}/suspend",
            new SuspendItemRequest { Reason = "Foto tidak sesuai barangnya." }))
            .EnsureSuccessStatusCode();

        var sesudah = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.True(sesudah!.ItemsStamp > sebelum.ItemsStamp);
        Assert.NotEqual(sebelum, sesudah);
    }

    [Fact]
    public async Task Penyewa_tidak_membawa_stempel_barang()
    {
        var seller = await api.VerifiedSellerAsync();
        await api.CreateItemAsync(seller.Client);

        var renter = await api.RenterAsync();

        var pulse = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Null(pulse!.ItemsStamp);
    }

    [Fact]
    public async Task Stempel_buku_besar_bergerak_saat_uang_bergerak()
    {
        var owner = await api.ClientAsOwnerAsync();

        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var sebelum = await owner.GetFromJsonAsync<PulseResponse>("/pulse");

        await api.SettleAsync(renter.Client, booking.Id);

        var sesudah = await owner.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.NotNull(sesudah!.LedgerStamp);
        Assert.True(sebelum!.LedgerStamp is null || sesudah.LedgerStamp > sebelum.LedgerStamp);
        Assert.NotEqual(sebelum, sesudah);
    }

    [Fact]
    public async Task Bukan_owner_tidak_membawa_stempel_buku_besar()
    {
        var admin  = await api.ClientAsAdminAsync();
        var seller = await api.VerifiedSellerAsync();

        var denyutAdmin  = await admin.GetFromJsonAsync<PulseResponse>("/pulse");
        var denyutPemilik = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Null(denyutAdmin!.LedgerStamp);
        Assert.Null(denyutPemilik!.LedgerStamp);
    }

    [Fact]
    public async Task Tanpa_token_ditolak()
    {
        var response = await api.CreateClient().GetAsync("/pulse");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Denyut_yang_sama_dibandingkan_sebagai_nilai()
    {
        var renter = await api.RenterAsync();

        var a = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        var b = await renter.Client.GetFromJsonAsync<PulseResponse>("/pulse");

        Assert.Equal(a, b);
    }
}
