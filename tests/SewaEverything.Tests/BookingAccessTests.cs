using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class BookingAccessTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int hariKe) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(600 + hariKe).AddHours(9);

    private sealed record Skenario(
        ApiFactory.UserContext Seller, ApiFactory.UserContext Renter, BookingResponse Booking);

    private async Task<Skenario> SiapkanAsync(int hariKe)
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(hariKe), Slot(hariKe + 2));

        return new Skenario(seller, renter, booking);
    }

    [Fact]
    public async Task Penyewa_dan_pemilik_barang_sama_sama_bisa_melihat()
    {
        var s = await SiapkanAsync(1);

        Assert.Equal(HttpStatusCode.OK,
            (await s.Renter.Client.GetAsync($"/bookings/{s.Booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await s.Seller.Client.GetAsync($"/bookings/{s.Booking.Id}")).StatusCode);
    }

    [Fact]
    public async Task Orang_luar_tidak_bisa_melihat_booking_orang_lain()
    {
        var s = await SiapkanAsync(5);

        var penyusupRenter = await api.RenterAsync();
        var penyusupSeller = await api.VerifiedSellerAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await penyusupRenter.Client.GetAsync($"/bookings/{s.Booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await penyusupSeller.Client.GetAsync($"/bookings/{s.Booking.Id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_bisa_melihat_booking_siapa_pun()
    {
        var s = await SiapkanAsync(10);
        var admin = await api.ClientAsAdminAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/bookings/{s.Booking.Id}")).StatusCode);
    }

    [Fact]
    public async Task Daftar_renter_hanya_berisi_booking_sendiri()
    {
        var s = await SiapkanAsync(15);
        var lain = await SiapkanAsync(20);

        var daftar = await s.Renter.Client
            .GetFromJsonAsync<PagedResponse<BookingResponse>>("/bookings");

        Assert.Contains(daftar!.Items, b => b.Id == s.Booking.Id);
        Assert.DoesNotContain(daftar.Items, b => b.Id == lain.Booking.Id);
    }

    [Fact]
    public async Task Daftar_seller_hanya_berisi_booking_untuk_barangnya()
    {
        var s = await SiapkanAsync(25);
        var lain = await SiapkanAsync(30);

        var daftar = await s.Seller.Client
            .GetFromJsonAsync<PagedResponse<BookingResponse>>("/bookings");

        Assert.Contains(daftar!.Items, b => b.Id == s.Booking.Id);
        Assert.DoesNotContain(daftar.Items, b => b.Id == lain.Booking.Id);
    }

    [Fact]
    public async Task Daftar_bisa_disaring_per_status()
    {
        var s = await SiapkanAsync(35);
        await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/approve", null);

        var confirmed = await s.Renter.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>(
            $"/bookings?status={BookingStatuses.Confirmed}");
        var pending = await s.Renter.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>(
            $"/bookings?status={BookingStatuses.Pending}");

        Assert.Contains(confirmed!.Items, b => b.Id == s.Booking.Id);
        Assert.DoesNotContain(pending!.Items, b => b.Id == s.Booking.Id);
    }

    [Fact]
    public async Task Status_tidak_dikenal_di_filter_ditolak()
    {
        var renter = await api.RenterAsync();

        var response = await renter.Client.GetAsync("/bookings?status=entahlah");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Seller_lain_tidak_bisa_menyetujui_booking_barang_orang()
    {
        var s = await SiapkanAsync(40);
        var penyusup = await api.VerifiedSellerAsync();

        var response = await penyusup.Client.PostAsync($"/bookings/{s.Booking.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Renter_tidak_bisa_menyetujui_bookingnya_sendiri()
    {
        var s = await SiapkanAsync(45);

        var response = await s.Renter.Client.PostAsync($"/bookings/{s.Booking.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Renter_tidak_bisa_menyerahkan_barang()
    {
        var s = await SiapkanAsync(50);
        await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/approve", null);

        var response = await s.Renter.Client.PostAsync($"/bookings/{s.Booking.Id}/handover", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Seller_tidak_bisa_memesan()
    {
        var seller = await api.VerifiedSellerAsync();
        var lain = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(lain.Client);

        var response = await api.BookAsync(seller.Client, item.Id, Slot(55), Slot(57));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tanpa_token_endpoint_booking_401()
    {
        var anonim = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonim.GetAsync($"/bookings/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Penyewa_maupun_pemilik_barang_sama_sama_boleh_membatalkan(bool olehRenter)
    {
        var s = await SiapkanAsync(olehRenter ? 60 : 65);

        var client = olehRenter ? s.Renter.Client : s.Seller.Client;

        var response = await client.PostAsJsonAsync(
            $"/bookings/{s.Booking.Id}/cancel", new CancelBookingRequest { Reason = "Uji" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Cancelled, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Orang_luar_tidak_bisa_membatalkan_booking_orang()
    {
        var s = await SiapkanAsync(70);
        var penyusup = await api.RenterAsync();

        var response = await penyusup.Client.PostAsJsonAsync(
            $"/bookings/{s.Booking.Id}/cancel", new CancelBookingRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(s.Booking.Id));
    }
}
