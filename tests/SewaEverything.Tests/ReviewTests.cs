using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReviewTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2200 + n).AddHours(9);

    private async Task<(ApiFactory.UserContext Seller, ApiFactory.UserContext Renter,
                        ItemDetailResponse Item, Guid BookingId)> CompletedAsync(int slot)
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var bookingId = await CompleteBookingAsync(seller, renter, item.Id, slot);
        return (seller, renter, item, bookingId);
    }

    private async Task<Guid> CompleteBookingAsync(
        ApiFactory.UserContext seller, ApiFactory.UserContext renter, Guid itemId, int slot)
    {
        var booking = await api.CreateBookingAsync(renter.Client, itemId, Slot(slot), Slot(slot + 2));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null)).EnsureSuccessStatusCode();
        await api.SettleAsync(renter.Client, booking.Id);
        (await seller.Client.PostAsync($"/bookings/{booking.Id}/handover", null)).EnsureSuccessStatusCode();
        (await seller.Client.PostAsync($"/bookings/{booking.Id}/return", null)).EnsureSuccessStatusCode();

        return booking.Id;
    }

    [Fact]
    public async Task Renter_menilai_sewa_yang_selesai()
    {
        var c = await CompletedAsync(1);

        var response = await c.Renter.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
            new CreateReviewRequest { Rating = 5, Comment = "Barang mulus, seller ramah." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var review = (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
        Assert.Equal(5, review.Rating);
        Assert.Equal(c.Item.Id, review.ItemId);

        var reviews = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{c.Item.Id}/reviews");

        Assert.Equal(1, reviews!.Count);
        Assert.Equal(5.0, reviews.Average);
        Assert.Single(reviews.Items);
        Assert.Equal("Barang mulus, seller ramah.", reviews.Items[0].Comment);

        var perBooking = await c.Renter.Client
            .GetFromJsonAsync<ReviewResponse>($"/bookings/{c.BookingId}/review");
        Assert.Equal(review.Id, perBooking!.Id);
    }

    [Fact]
    public async Task Komentar_boleh_kosong()
    {
        var c = await CompletedAsync(11);

        var response = await c.Renter.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
            new CreateReviewRequest { Rating = 4 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var review = (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
        Assert.Null(review.Comment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Rating_di_luar_1_sampai_5_ditolak(int rating)
    {
        var c = await CompletedAsync(20 + rating + 2);

        var response = await c.Renter.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
            new CreateReviewRequest { Rating = rating });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hanya_penyewa_yang_boleh_menilai()
    {
        var c = await CompletedAsync(31);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.Seller.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
                new CreateReviewRequest { Rating = 5 })).StatusCode);

        var orangLain = await api.RenterAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await orangLain.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
                new CreateReviewRequest { Rating = 5 })).StatusCode);
    }

    [Fact]
    public async Task Sewa_yang_belum_selesai_tidak_bisa_dinilai()
    {
        var s = await api.ActiveBookingAsync(Slot(41), Slot(43));

        var response = await s.Renter.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/reviews",
            new CreateReviewRequest { Rating = 5 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Satu_sewa_hanya_bisa_dinilai_sekali()
    {
        var c = await CompletedAsync(51);

        (await c.Renter.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
            new CreateReviewRequest { Rating = 5 })).EnsureSuccessStatusCode();

        var kedua = await c.Renter.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
            new CreateReviewRequest { Rating = 1 });

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);

        var reviews = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{c.Item.Id}/reviews");
        Assert.Equal(1, reviews!.Count);
        Assert.Equal(5.0, reviews.Average);
    }

    [Fact]
    public async Task Rata_rata_barang_menghitung_semua_penilaiannya()
    {
        var seller  = await api.VerifiedSellerAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();

        var bookingA = await CompleteBookingAsync(seller, renterA, item.Id, 61);
        var bookingB = await CompleteBookingAsync(seller, renterB, item.Id, 65);

        (await renterA.Client.PostAsJsonAsync($"/bookings/{bookingA}/reviews",
            new CreateReviewRequest { Rating = 4 })).EnsureSuccessStatusCode();
        (await renterB.Client.PostAsJsonAsync($"/bookings/{bookingB}/reviews",
            new CreateReviewRequest { Rating = 2 })).EnsureSuccessStatusCode();

        var reviews = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{item.Id}/reviews");

        Assert.Equal(2, reviews!.Count);
        Assert.Equal(3.0, reviews.Average);
        Assert.Equal(2, reviews.Items.Count);
    }

    [Fact]
    public async Task Rincian_bintang_menghitung_tiap_nilai()
    {
        var seller  = await api.VerifiedSellerAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var renterC = await api.RenterAsync();

        var a = await CompleteBookingAsync(seller, renterA, item.Id, 71);
        var b = await CompleteBookingAsync(seller, renterB, item.Id, 75);
        var c = await CompleteBookingAsync(seller, renterC, item.Id, 79);

        (await renterA.Client.PostAsJsonAsync($"/bookings/{a}/reviews",
            new CreateReviewRequest { Rating = 5 })).EnsureSuccessStatusCode();
        (await renterB.Client.PostAsJsonAsync($"/bookings/{b}/reviews",
            new CreateReviewRequest { Rating = 5 })).EnsureSuccessStatusCode();
        (await renterC.Client.PostAsJsonAsync($"/bookings/{c}/reviews",
            new CreateReviewRequest { Rating = 3 })).EnsureSuccessStatusCode();

        var reviews = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{item.Id}/reviews");

        Assert.Equal(5, reviews!.Buckets.Count);
        Assert.Equal(2, reviews.Buckets.Single(x => x.Rating == 5).Count);
        Assert.Equal(1, reviews.Buckets.Single(x => x.Rating == 3).Count);
        Assert.Equal(0, reviews.Buckets.Single(x => x.Rating == 1).Count);
        Assert.Equal(3, reviews.Buckets.Sum(x => x.Count));
        Assert.Null(reviews.Rating);
    }

    [Fact]
    public async Task Saringan_bintang_memangkas_daftar_tanpa_mengubah_rata_rata()
    {
        var seller  = await api.VerifiedSellerAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();

        var a = await CompleteBookingAsync(seller, renterA, item.Id, 83);
        var b = await CompleteBookingAsync(seller, renterB, item.Id, 87);

        (await renterA.Client.PostAsJsonAsync($"/bookings/{a}/reviews",
            new CreateReviewRequest { Rating = 5 })).EnsureSuccessStatusCode();
        (await renterB.Client.PostAsJsonAsync($"/bookings/{b}/reviews",
            new CreateReviewRequest { Rating = 1 })).EnsureSuccessStatusCode();

        var lima = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{item.Id}/reviews?rating=5");

        Assert.Equal(5, lima!.Rating);
        Assert.Equal(1, lima.Count);
        Assert.Single(lima.Items);
        Assert.Equal(5, lima.Items[0].Rating);
        Assert.Equal(3.0, lima.Average);
        Assert.Equal(2, lima.Buckets.Sum(x => x.Count));

        var dua = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{item.Id}/reviews?rating=2");

        Assert.Equal(0, dua!.Count);
        Assert.Empty(dua.Items);
        Assert.Equal(3.0, dua.Average);
    }

    [Fact]
    public async Task Saringan_bintang_di_luar_satu_sampai_lima_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        var nol = await api.CreateClient().GetAsync($"/items/{item.Id}/reviews?rating=0");
        var enam = await api.CreateClient().GetAsync($"/items/{item.Id}/reviews?rating=6");

        Assert.Equal(HttpStatusCode.BadRequest, nol.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, enam.StatusCode);
    }

    [Fact]
    public async Task Barang_tanpa_penilaian_mengembalikan_ringkasan_kosong()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        var reviews = await api.CreateClient()
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{item.Id}/reviews");

        Assert.Equal(0, reviews!.Count);
        Assert.Null(reviews.Average);
        Assert.Empty(reviews.Items);
    }

    [Fact]
    public async Task Penilaian_barang_tersembunyi_tidak_terlihat_publik()
    {
        var c = await CompletedAsync(71);

        (await c.Renter.Client.PostAsJsonAsync($"/bookings/{c.BookingId}/reviews",
            new CreateReviewRequest { Rating = 5 })).EnsureSuccessStatusCode();

        (await c.Seller.Client.PutAsJsonAsync($"/items/{c.Item.Id}", new UpdateItemRequest
        {
            Title         = c.Item.Title,
            Category      = c.Item.Category,
            Description   = c.Item.Description,
            Price         = c.Item.Price,
            PriceUnit     = c.Item.PriceUnit,
            DepositAmount = c.Item.DepositAmount,
            Status        = ItemStatuses.Inactive
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound,
            (await api.CreateClient().GetAsync($"/items/{c.Item.Id}/reviews")).StatusCode);

        var pemilik = await c.Seller.Client
            .GetFromJsonAsync<ItemReviewsResponse>($"/items/{c.Item.Id}/reviews");
        Assert.Equal(1, pemilik!.Count);
    }
}
