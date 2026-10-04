using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class PaymentInstructionTests(ApiFactory api)
{
    private static readonly DateTimeOffset Mulai = DateTimeOffset.UtcNow.Date.AddDays(3);
    private static readonly DateTimeOffset Selesai = Mulai.AddDays(2);

    [Fact]
    public async Task Sebelum_ada_tagihan_dijawab_404()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var response = await renter.Client.GetAsync($"/bookings/{booking.Id}/payment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tagihan_yang_sudah_dibuat_dapat_dibaca_lagi_tanpa_membuat_yang_baru()
    {
        var (renter, booking) = await SiapBayarAsync();

        var dibuat = await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.Qris });
        dibuat.EnsureSuccessStatusCode();
        var pertama = await dibuat.Content.ReadFromJsonAsync<PaymentInstructionResponse>();

        var dibaca = await renter.Client.GetFromJsonAsync<PaymentInstructionResponse>(
            $"/bookings/{booking.Id}/payment");

        Assert.Equal(pertama!.OrderId, dibaca!.OrderId);
        Assert.Equal(pertama.Channel, dibaca.Channel);
        Assert.Equal(pertama.Amount, dibaca.Amount);
        Assert.Equal(pertama.QrString, dibaca.QrString);
        Assert.Equal(pertama.QrImageUrl, dibaca.QrImageUrl);
    }

    [Fact]
    public async Task Membaca_tagihan_tidak_menambah_baris_buku_besar()
    {
        var (renter, booking) = await SiapBayarAsync();

        (await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.Qris })).EnsureSuccessStatusCode();

        var sebelum = await renter.Client.GetFromJsonAsync<BookingLedgerResponse>(
            $"/bookings/{booking.Id}/ledger");

        for (var i = 0; i < 3; i++)
        {
            (await renter.Client.GetAsync($"/bookings/{booking.Id}/payment"))
                .EnsureSuccessStatusCode();
        }

        var sesudah = await renter.Client.GetFromJsonAsync<BookingLedgerResponse>(
            $"/bookings/{booking.Id}/ledger");

        Assert.Equal(sebelum!.Entries.Count, sesudah!.Entries.Count);
    }

    [Fact]
    public async Task QRIS_membawa_URL_gambar_QR()
    {
        var (renter, booking) = await SiapBayarAsync();

        var response = await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.Qris });
        response.EnsureSuccessStatusCode();

        var instruction = await response.Content.ReadFromJsonAsync<PaymentInstructionResponse>();

        Assert.False(string.IsNullOrWhiteSpace(instruction!.QrImageUrl));
    }

    [Fact]
    public async Task Channel_tanpa_QR_tidak_mengarang_URL_gambar()
    {
        var (renter, booking) = await SiapBayarAsync();

        var response = await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.VaBca });
        response.EnsureSuccessStatusCode();

        var instruction = await response.Content.ReadFromJsonAsync<PaymentInstructionResponse>();

        Assert.Null(instruction!.QrImageUrl);
        Assert.False(string.IsNullOrWhiteSpace(instruction.VirtualAccountNumber));
    }

    [Fact]
    public async Task Pemilik_barang_boleh_membaca_tagihan_sewanya()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        await api.AddPayoutAccountAsync(renter.Client);

        (await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.Qris })).EnsureSuccessStatusCode();

        var response = await seller.Client.GetAsync($"/bookings/{booking.Id}/payment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Orang_luar_tidak_dapat_membaca_tagihan_orang_lain()
    {
        var (renter, booking) = await SiapBayarAsync();

        (await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.Qris })).EnsureSuccessStatusCode();

        var penyusup = await api.RenterAsync();

        var response = await penyusup.Client.GetAsync($"/bookings/{booking.Id}/payment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tanpa_token_ditolak()
    {
        var (_, booking) = await SiapBayarAsync();

        var response = await api.CreateClient().GetAsync($"/bookings/{booking.Id}/payment");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(ApiFactory.UserContext Renter, BookingResponse Booking)> SiapBayarAsync()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Mulai, Selesai);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        await api.AddPayoutAccountAsync(renter.Client);

        return (renter, booking);
    }
}
