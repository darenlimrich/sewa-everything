using System.Net;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ApproveRequiresPayoutTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2000 + n).AddHours(9);

    [Fact]
    public async Task Approve_ditolak_kalau_seller_belum_punya_rekening()
    {
        var seller = await api.SellerAsync();
        await api.VerifySellerAsync(seller.Id);

        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(1), Slot(3));

        var response = await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BookingStatuses.Pending, await api.StatusOfAsync(booking.Id));
    }

    [Fact]
    public async Task Approve_diterima_setelah_rekening_didaftarkan()
    {
        var seller = await api.SellerAsync();
        await api.VerifySellerAsync(seller.Id);

        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(11), Slot(13));

        Assert.Equal(HttpStatusCode.Conflict,
            (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null)).StatusCode);

        await api.AddPayoutAccountAsync(seller.Client);

        var response = await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookingStatuses.Confirmed, await api.StatusOfAsync(booking.Id));
    }
}
