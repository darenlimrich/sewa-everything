using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class CartTests(ApiFactory api)
{
    private static readonly DateTimeOffset Besok =
        DateTimeOffset.UtcNow.Date.AddDays(1);

    private static AddCartItemRequest Baris(Guid itemId, int mulaiHari = 1, int lamaHari = 3) => new()
    {
        ItemId  = itemId,
        StartAt = Besok.AddDays(mulaiHari).UtcDateTime,
        EndAt   = Besok.AddDays(mulaiHari + lamaHari).UtcDateTime
    };

    private async Task<(ApiFactory.UserContext Renter, ItemDetailResponse Item)> SiapAsync()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        return (renter, item);
    }

    [Fact]
    public async Task Keranjang_baru_kosong()
    {
        var renter = await api.RenterAsync();

        var cart = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");

        Assert.Equal(0, cart!.Count);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public async Task Penyewa_dapat_menambah_lalu_membacanya_kembali()
    {
        var (renter, item) = await SiapAsync();

        var response = await renter.Client.PostAsJsonAsync("/cart", Baris(item.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cart = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");

        Assert.Equal(1, cart!.Count);
        Assert.Equal(item.Id, cart.Items[0].ItemId);
        Assert.Equal(item.Title, cart.Items[0].ItemTitle);
        Assert.Equal(item.Price, cart.Items[0].Price);
        Assert.True(cart.Items[0].Available);
    }

    [Fact]
    public async Task Menambah_barang_yang_sama_MENGGANTI_tanggalnya_bukan_menumpuk()
    {
        var (renter, item) = await SiapAsync();

        (await renter.Client.PostAsJsonAsync("/cart", Baris(item.Id, mulaiHari: 1)))
            .EnsureSuccessStatusCode();
        (await renter.Client.PostAsJsonAsync("/cart", Baris(item.Id, mulaiHari: 20)))
            .EnsureSuccessStatusCode();

        var cart = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");

        Assert.Equal(1, cart!.Count);
        Assert.Equal(Besok.AddDays(20).UtcDateTime, cart.Items[0].StartAt);
    }

    [Fact]
    public async Task Keranjang_TIDAK_menahan_slot_untuk_penyewa_lain()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);

        (await renterA.Client.PostAsJsonAsync("/cart", Baris(item.Id)))
            .EnsureSuccessStatusCode();

        var booking = await api.BookAsync(
            renterB.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
    }

    [Fact]
    public async Task Baris_yang_slotnya_sudah_diambil_orang_lain_ditandai_tidak_tersedia()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);

        (await renterA.Client.PostAsJsonAsync("/cart", Baris(item.Id)))
            .EnsureSuccessStatusCode();

        await api.CreateBookingAsync(renterB.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var cart = await renterA.Client.GetFromJsonAsync<CartResponse>("/cart");

        Assert.Equal(1, cart!.Count);
        Assert.False(cart.Items[0].Available);
    }

    [Fact]
    public async Task Keranjang_hanya_berisi_milik_sendiri()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);

        (await renterA.Client.PostAsJsonAsync("/cart", Baris(item.Id)))
            .EnsureSuccessStatusCode();

        var cartB = await renterB.Client.GetFromJsonAsync<CartResponse>("/cart");

        Assert.Equal(0, cartB!.Count);
    }

    [Fact]
    public async Task Baris_milik_orang_lain_tidak_dapat_dihapus()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);

        var added = await renterA.Client.PostAsJsonAsync("/cart", Baris(item.Id));
        var line = await added.Content.ReadFromJsonAsync<CartItemResponse>();

        var response = await renterB.Client.DeleteAsync($"/cart/{line!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var cartA = await renterA.Client.GetFromJsonAsync<CartResponse>("/cart");
        Assert.Equal(1, cartA!.Count);
    }

    [Fact]
    public async Task Pemilik_dapat_menghapus_barisnya_sendiri()
    {
        var (renter, item) = await SiapAsync();

        var added = await renter.Client.PostAsJsonAsync("/cart", Baris(item.Id));
        var line = await added.Content.ReadFromJsonAsync<CartItemResponse>();

        var response = await renter.Client.DeleteAsync($"/cart/{line!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var cart = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");
        Assert.Equal(0, cart!.Count);
    }

    [Fact]
    public async Task Rentang_terbalik_ditolak()
    {
        var (renter, item) = await SiapAsync();

        var response = await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId  = item.Id,
            StartAt = Besok.AddDays(5).UtcDateTime,
            EndAt   = Besok.AddDays(2).UtcDateTime
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tanggal_yang_sudah_lewat_ditolak()
    {
        var (renter, item) = await SiapAsync();

        var response = await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId  = item.Id,
            StartAt = DateTime.UtcNow.AddDays(-3),
            EndAt   = DateTime.UtcNow.AddDays(-1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Barang_yang_pemiliknya_belum_terverifikasi_tidak_dapat_dimasukkan()
    {
        var seller = await api.SellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        var response = await renter.Client.PostAsJsonAsync("/cart", Baris(item.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Seller)]
    public async Task Bukan_penyewa_tidak_punya_keranjang(string role)
    {
        var client = await api.ClientAsAsync(role);

        var list = await client.GetAsync("/cart");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Tanpa_token_ditolak()
    {
        var response = await api.CreateClient().GetAsync("/cart");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
