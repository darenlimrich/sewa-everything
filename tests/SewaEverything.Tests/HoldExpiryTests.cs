using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class HoldExpiryTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int hariKe) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(500 + hariKe).AddHours(9);

    [Fact]
    public async Task Hold_pending_yang_kedaluwarsa_dibatalkan()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(1), Slot(3));

        await api.ExpireHoldAsync(booking.Id);
        await api.SweepHoldsAsync();

        Assert.Equal(BookingStatuses.Cancelled, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Hold_confirmed_yang_kedaluwarsa_juga_dibatalkan()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(5), Slot(7));

        var disetujui = await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);
        disetujui.EnsureSuccessStatusCode();

        await api.ExpireHoldAsync(booking.Id);
        await api.SweepHoldsAsync();

        Assert.Equal(BookingStatuses.Cancelled, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Hold_yang_belum_jatuh_tempo_tidak_disentuh()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(10), Slot(12));

        await api.SweepHoldsAsync();

        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Booking_active_tidak_ikut_tersapu()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(15), Slot(17));

        await api.DriveToStatusAsync(booking.Id, BookingStatuses.Active);
        await api.ExpireHoldAsync(booking.Id);
        await api.SweepHoldsAsync();

        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Slot_bisa_dipesan_lagi_setelah_hold_dilepas()
    {
        var seller = await api.VerifiedSellerAsync();
        var pertama = await api.RenterAsync();
        var kedua = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(pertama.Client, item.Id, Slot(20), Slot(22));

        var ditolak = await api.BookAsync(kedua.Client, item.Id, Slot(20), Slot(22));
        Assert.Equal(HttpStatusCode.Conflict, ditolak.StatusCode);

        await api.ExpireHoldAsync(booking.Id);
        Assert.Equal(1, await api.SweepHoldsAsync());

        var diterima = await api.BookAsync(kedua.Client, item.Id, Slot(20), Slot(22));
        Assert.Equal(HttpStatusCode.Created, diterima.StatusCode);
    }

    [Fact]
    public async Task Barang_muncul_lagi_di_pencarian_setelah_hold_dilepas()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var kategori = $"Uji-Hold-{Guid.NewGuid():N}";
        var item = await api.CreateItemAsync(seller.Client, category: kategori);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(25), Slot(27));

        var query = $"/items?category={kategori}" +
                    $"&availableFrom={Uri.EscapeDataString(Slot(25).ToString("O"))}" +
                    $"&availableTo={Uri.EscapeDataString(Slot(27).ToString("O"))}";

        var sebelum = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(query);
        Assert.Empty(sebelum!.Items);

        await api.ExpireHoldAsync(booking.Id);
        await api.SweepHoldsAsync();

        var sesudah = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(query);
        Assert.Single(sesudah!.Items);
    }

    [Fact]
    public async Task Pembatalan_otomatis_mencantumkan_alasan_dan_menghapus_tenggat()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(30), Slot(32));

        await api.ExpireHoldAsync(booking.Id);
        await api.SweepHoldsAsync();

        var sesudah = await renter.Client.GetFromJsonAsync<BookingResponse>($"/bookings/{booking.Id}");

        Assert.Equal(BookingStatuses.Cancelled, sesudah!.Status);
        Assert.Contains("kedaluwarsa", sesudah.CancelledReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sesudah.HoldExpiresAt);
    }

    [Fact]
    public async Task Sapuan_kedua_tidak_melepas_apa_apa_lagi()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(35), Slot(37));

        await api.ExpireHoldAsync(booking.Id);

        Assert.Equal(1, await api.SweepHoldsAsync());
        Assert.Equal(0, await api.SweepHoldsAsync());
    }

    [Fact]
    public async Task Approve_setelah_tenggat_lewat_ditolak_walau_belum_tersapu()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(40), Slot(42));

        await api.ExpireHoldAsync(booking.Id);

        var response = await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Membatalkan_setelah_tenggat_lewat_tetap_boleh()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(45), Slot(47));

        await api.ExpireHoldAsync(booking.Id);

        var response = await renter.Client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/cancel", new CancelBookingRequest { Reason = "Batal saja" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Cancelled, await api.StatusOfAsync(booking.Id));
    }
}
