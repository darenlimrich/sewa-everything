using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReturnDueSweeperTests(ApiFactory api)
{
    private static DateTimeOffset DaysFromNow(int d) =>
        new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).AddDays(d);

    private static PaymentResponse Row(IReadOnlyList<PaymentResponse> entries, string kind) =>
        entries.Single(e => e.Kind == kind);

    [Fact]
    public async Task Sewa_yang_lewat_jendela_diselesaikan_otomatis_dengan_deposit_penuh()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m);

        var bookingId = await api.SeedActivePaidBookingAsync(
            item.Id, renter.Id, DaysFromNow(-62), DaysFromNow(-60));

        var completed = await api.SweepReturnsAsync();

        Assert.True(completed >= 1);
        Assert.Equal(BookingStatuses.Completed, await api.StatusOfAsync(bookingId));

        var entries = await api.LedgerEntriesAsync(seller.Client, bookingId);

        Assert.Equal(500_000m, Row(entries, PaymentKinds.DepositRefund).Amount);
        Assert.Equal(190_000m, Row(entries, PaymentKinds.SellerPayout).Amount);
        Assert.Equal(10_000m,  Row(entries, PaymentKinds.PlatformFee).Amount);
        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.DepositForfeit);
    }

    [Fact]
    public async Task Sewa_yang_masih_berjalan_tidak_disentuh()
    {
        var s = await api.ActiveBookingAsync(DaysFromNow(400), DaysFromNow(402));

        await api.SweepReturnsAsync();

        Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(s.Booking.Id));
    }

    [Fact]
    public async Task Sewa_yang_baru_berakhir_dalam_jendela_belum_diselesaikan()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, deposit: 500_000m);

        var bookingId = await api.SeedActivePaidBookingAsync(
            item.Id, renter.Id, DaysFromNow(-7), DaysFromNow(-5));

        var sebelum = await api.SetReturnWindowDaysAsync(30);
        try
        {
            await api.SweepReturnsAsync();
            Assert.Equal(BookingStatuses.Active, await api.StatusOfAsync(bookingId));
        }
        finally
        {
            await api.SetReturnWindowDaysAsync(sebelum);
        }
    }

    [Fact]
    public async Task Sapuan_kedua_tidak_menyelesaikan_ulang()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, deposit: 500_000m);

        var bookingId = await api.SeedActivePaidBookingAsync(
            item.Id, renter.Id, DaysFromNow(-62), DaysFromNow(-60));

        await api.SweepReturnsAsync();
        await api.SweepReturnsAsync();

        var entries = await api.LedgerEntriesAsync(seller.Client, bookingId);
        Assert.Single(entries, e => e.Kind == PaymentKinds.SellerPayout);
        Assert.Single(entries, e => e.Kind == PaymentKinds.DepositRefund);
    }
}
