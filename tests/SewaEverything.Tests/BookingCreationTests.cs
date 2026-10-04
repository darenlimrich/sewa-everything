using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class BookingCreationTests(ApiFactory api)
{
    private static DateTimeOffset Soon(int days) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days).AddHours(9);

    [Fact]
    public async Task Renter_bisa_memesan_dan_hasilnya_pending_dengan_hold()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client, price: 150_000m, priceUnit: PriceUnits.Day);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Soon(5), Soon(8));

        Assert.Equal(BookingStatuses.Pending, booking.Status);
        Assert.Equal(renter.Id, booking.RenterId);
        Assert.Equal(seller.Id, booking.SellerId);
        Assert.NotNull(booking.HoldExpiresAt);
        Assert.True(booking.HoldExpiresAt > DateTime.UtcNow,
            "hold booking baru harus jatuh tempo di masa depan");
    }

    [Fact]
    public async Task Total_palsu_dari_klien_diabaikan_server_menghitung_sendiri()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();

        var item = await api.CreateItemAsync(
            seller.Client, price: 200_000m, priceUnit: PriceUnits.Day, deposit: 1_000_000m);

        var response = await renter.Client.PostAsJsonAsync("/bookings", new
        {
            itemId   = item.Id,
            startsAt = Soon(10),
            endsAt   = Soon(13),

            totalRent         = 1m,
            durationUnits     = 1,
            priceSnapshot     = 1m,
            depositAmount     = 0m,
            platformFeeRate   = 0m,
            platformFeeAmount = 0m,
            renterTotal       = 1m,
            sellerGross       = 999_999m,
            status            = BookingStatuses.Completed
        });

        response.EnsureSuccessStatusCode();
        var booking = (await response.Content.ReadFromJsonAsync<BookingResponse>())!;

        Assert.Equal(3, booking.DurationUnits);
        Assert.Equal(600_000m, booking.TotalRent);
        Assert.Equal(1_000_000m, booking.DepositAmount);
        Assert.Equal(200_000m, booking.PriceSnapshot);
        Assert.Equal(BookingStatuses.Pending, booking.Status);
        Assert.NotEqual(999_999m, booking.SellerGross);
    }

    [Theory]
    [InlineData(PriceUnits.Hour, 90, 2)]
    [InlineData(PriceUnits.Hour, 60, 1)]
    [InlineData(PriceUnits.Day, 60 * 25, 2)]
    [InlineData(PriceUnits.Day, 60 * 24, 1)]
    [InlineData(PriceUnits.Day, 30, 1)]
    [InlineData(PriceUnits.Week, 60 * 24 * 10, 2)]
    [InlineData(PriceUnits.Week, 60 * 24 * 7, 1)]
    [InlineData(PriceUnits.Month, 60 * 24 * 45, 2)]
    [InlineData(PriceUnits.Month, 60 * 24 * 30, 1)]
    public async Task Durasi_dibulatkan_ke_atas(string unit, int menit, int diharapkan)
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();

        var item = await api.CreateItemAsync(seller.Client, price: 10_000m, priceUnit: unit);

        var mulai = Soon(20);
        var booking = await api.CreateBookingAsync(
            renter.Client, item.Id, mulai, mulai.AddMinutes(menit));

        Assert.Equal(diharapkan, booking.DurationUnits);
        Assert.Equal(10_000m * diharapkan, booking.TotalRent);
        Assert.Equal(unit, booking.PriceUnitSnapshot);
    }

    [Fact]
    public async Task Komisi_mode_deduct_dipotong_dari_seller()
    {
        await SetCommissionAsync(0.10m, CommissionModes.Deduct);

        try
        {
            var seller = await api.VerifiedSellerAsync();
            var renter = await api.RenterAsync();
            var item = await api.CreateItemAsync(
                seller.Client, price: 100_000m, priceUnit: PriceUnits.Day, deposit: 50_000m);

            var booking = await api.CreateBookingAsync(renter.Client, item.Id, Soon(30), Soon(32));

            Assert.Equal(200_000m, booking.TotalRent);
            Assert.Equal(20_000m, booking.PlatformFeeAmount);
            Assert.Equal(250_000m, booking.RenterTotal);
            Assert.Equal(180_000m, booking.SellerGross);
        }
        finally
        {
            await SetCommissionAsync(0.05m, CommissionModes.Deduct);
        }
    }

    [Fact]
    public async Task Komisi_mode_on_top_ditambahkan_ke_renter()
    {
        await SetCommissionAsync(0.10m, CommissionModes.OnTop);

        try
        {
            var seller = await api.VerifiedSellerAsync();
            var renter = await api.RenterAsync();
            var item = await api.CreateItemAsync(
                seller.Client, price: 100_000m, priceUnit: PriceUnits.Day, deposit: 50_000m);

            var booking = await api.CreateBookingAsync(renter.Client, item.Id, Soon(35), Soon(37));

            Assert.Equal(200_000m, booking.TotalRent);
            Assert.Equal(20_000m, booking.PlatformFeeAmount);
            Assert.Equal(270_000m, booking.RenterTotal);
            Assert.Equal(200_000m, booking.SellerGross);
        }
        finally
        {
            await SetCommissionAsync(0.05m, CommissionModes.Deduct);
        }
    }

    [Fact]
    public async Task Pembulatan_komisi_sama_dengan_pembulatan_postgres()
    {
        await SetCommissionAsync(0.0333m, CommissionModes.Deduct);

        try
        {
            var seller = await api.VerifiedSellerAsync();
            var renter = await api.RenterAsync();
            var item = await api.CreateItemAsync(seller.Client, price: 10_001m, priceUnit: PriceUnits.Day);

            var booking = await api.CreateBookingAsync(renter.Client, item.Id, Soon(40), Soon(43));

            Assert.Equal(30_003m, booking.TotalRent);
            Assert.Equal(999.10m, booking.PlatformFeeAmount);
        }
        finally
        {
            await SetCommissionAsync(0.05m, CommissionModes.Deduct);
        }
    }

    [Fact]
    public async Task Rentang_terbalik_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var response = await api.BookAsync(renter.Client, item.Id, Soon(50), Soon(48));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Memesan_waktu_lampau_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var response = await api.BookAsync(
            renter.Client, item.Id, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Memesan_barang_nonaktif_404()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        await seller.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = item.Title,
            Category      = item.Category,
            Price         = item.Price,
            PriceUnit     = item.PriceUnit,
            DepositAmount = item.DepositAmount,
            Status        = ItemStatuses.Inactive
        });

        var response = await api.BookAsync(renter.Client, item.Id, Soon(55), Soon(57));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Memesan_barang_seller_belum_terverifikasi_404()
    {
        var seller = await api.SellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var response = await api.BookAsync(renter.Client, item.Id, Soon(60), Soon(62));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Memesan_barang_tidak_ada_404()
    {
        var renter = await api.RenterAsync();

        var response = await api.BookAsync(renter.Client, Guid.NewGuid(), Soon(65), Soon(67));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bentrok_dengan_booking_lain_ditolak_409()
    {
        var seller = await api.VerifiedSellerAsync();
        var pertama = await api.RenterAsync();
        var kedua = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(pertama.Client, item.Id, Soon(70), Soon(74));

        var response = await api.BookAsync(kedua.Client, item.Id, Soon(72), Soon(76));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Bentrok_dengan_blackout_ditolak_409()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        await seller.Client.PostAsJsonAsync($"/items/{item.Id}/blackouts", new CreateBlackoutRequest
        {
            StartsAt = Soon(80),
            EndsAt   = Soon(84),
            Reason   = "Diservis"
        });

        var response = await api.BookAsync(renter.Client, item.Id, Soon(82), Soon(86));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Booking_bersambungan_diterima()
    {
        var seller = await api.VerifiedSellerAsync();
        var pertama = await api.RenterAsync();
        var kedua = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var batas = Soon(90);

        await api.CreateBookingAsync(pertama.Client, item.Id, batas.AddDays(-2), batas);
        var response = await api.BookAsync(kedua.Client, item.Id, batas, batas.AddDays(2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Nominal_melebihi_kapasitas_kolom_ditolak_400()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();

        var item = await api.CreateItemAsync(
            seller.Client, price: 900_000_000_000m, priceUnit: PriceUnits.Day, deposit: 0m);

        var mulai = Soon(100);
        var response = await api.BookAsync(renter.Client, item.Id, mulai, mulai.AddDays(30));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SetCommissionAsync(decimal rate, string mode)
    {
        var owner = await api.ClientAsOwnerAsync();

        var response = await owner.PutAsJsonAsync("/owner/settings", new UpdatePlatformSettingsRequest
        {
            CommissionRate   = rate,
            CommissionMode   = mode,
            ApprovalMinutes  = 1440,
            PaymentMinutes   = 60,
            ReturnWindowDays = 3
        });

        response.EnsureSuccessStatusCode();
    }
}
