using System.Net.Http.Json;
using System.Text.RegularExpressions;
using SewaEverything.Contracts;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReferenceCodeTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int n) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2400 + n).AddHours(9);

    private const string Abjad = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly Regex NomorSewa = new($"^SW-[{Abjad}]{{6}}$");
    private static readonly Regex NomorTransaksi = new($"^TR-[{Abjad}]{{6}}$");

    [Fact]
    public async Task Sewa_baru_langsung_punya_nomor_berformat_benar()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(1), Slot(3));

        Assert.Matches(NomorSewa, booking.Reference);
    }

    [Fact]
    public async Task Nomor_sewa_ikut_di_detail_dan_di_daftar()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(11), Slot(13));

        var detail = await renter.Client.GetFromJsonAsync<BookingResponse>($"/bookings/{booking.Id}");
        Assert.Equal(booking.Reference, detail!.Reference);

        var daftar = await renter.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>("/bookings");
        Assert.Equal(booking.Reference, daftar!.Items.Single(b => b.Id == booking.Id).Reference);
    }

    [Fact]
    public async Task Nomor_sewa_tidak_berubah_saat_statusnya_berubah()
    {
        var s = await api.ActiveBookingAsync(Slot(21), Slot(23));

        var sesudah = await s.Renter.Client.GetFromJsonAsync<BookingResponse>($"/bookings/{s.Booking.Id}");

        Assert.Equal(s.Booking.Reference, sesudah!.Reference);
        Assert.Matches(NomorSewa, sesudah.Reference);
    }

    [Fact]
    public async Task Nomor_sewa_berbeda_antar_sewa()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        var nomor = new List<string>();

        for (var i = 0; i < 6; i++)
        {
            var booking = await api.CreateBookingAsync(
                renter.Client, item.Id, Slot(30 + i * 3), Slot(31 + i * 3));

            nomor.Add(booking.Reference);
        }

        Assert.Equal(nomor.Count, nomor.Distinct().Count());
    }

    [Fact]
    public async Task Nomor_tidak_pernah_memuat_huruf_yang_mudah_tertukar()
    {
        var s = await api.ActiveBookingAsync(Slot(61), Slot(63));

        var kode = new List<string> { s.Booking.Reference };
        kode.AddRange((await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id)).Select(e => e.Reference));

        Assert.NotEmpty(kode);

        foreach (var k in kode)
        {
            Assert.DoesNotContain('I', k);
            Assert.DoesNotContain('O', k);
            Assert.DoesNotContain('0', k);
            Assert.DoesNotContain('1', k);
        }
    }

    [Fact]
    public async Task Tiap_baris_buku_besar_punya_nomor_transaksinya_sendiri()
    {
        var s = await api.ActiveBookingAsync(Slot(71), Slot(73));

        var baris = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        Assert.True(baris.Count >= 2, "Sewa lunas selalu punya minimal baris sewa dan deposit.");
        Assert.All(baris, b => Assert.Matches(NomorTransaksi, b.Reference));
        Assert.Equal(baris.Count, baris.Select(b => b.Reference).Distinct().Count());
    }

    [Fact]
    public async Task Buku_besar_owner_membawa_nomor_transaksi_dan_nomor_sewanya()
    {
        var s = await api.ActiveBookingAsync(Slot(81), Slot(83));
        var owner = await api.ClientAsOwnerAsync();

        var ledger = await owner.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            "/owner/transactions?pageSize=100");

        var baris = ledger!.Items.Where(t => t.BookingId == s.Booking.Id).ToList();

        Assert.NotEmpty(baris);
        Assert.All(baris, t =>
        {
            Assert.Matches(NomorTransaksi, t.Reference);
            Assert.Equal(s.Booking.Reference, t.BookingReference);
        });
    }

    [Fact]
    public async Task Antrean_pencairan_membawa_kedua_nomor()
    {
        var s = await api.ActiveBookingAsync(Slot(91), Slot(93));

        (await s.Seller.Client.PostAsync($"/bookings/{s.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        var admin = await api.ClientAsAdminAsync();
        var antrean = await admin.GetFromJsonAsync<List<PendingPayoutResponse>>("/admin/payouts/pending");

        var milikSewaIni = antrean!.Where(p => p.BookingId == s.Booking.Id).ToList();

        Assert.NotEmpty(milikSewaIni);
        Assert.All(milikSewaIni, p =>
        {
            Assert.Matches(NomorTransaksi, p.Reference);
            Assert.Equal(s.Booking.Reference, p.BookingReference);
        });
    }

    [Fact]
    public async Task Antrean_sengketa_membawa_nomor_sewanya()
    {
        var s = await api.ActiveBookingAsync(Slot(101), Slot(103));

        (await s.Seller.Client.PostAsJsonAsync($"/bookings/{s.Booking.Id}/dispute",
            new RaiseDisputeRequest { Reason = "Lensa tergores saat dikembalikan." }))
            .EnsureSuccessStatusCode();

        var admin = await api.ClientAsAdminAsync();
        var antrean = await admin.GetFromJsonAsync<List<DisputeResponse>>("/admin/disputes");

        var sengketa = antrean!.Single(d => d.BookingId == s.Booking.Id);

        Assert.Equal(s.Booking.Reference, sengketa.BookingReference);
    }
}
