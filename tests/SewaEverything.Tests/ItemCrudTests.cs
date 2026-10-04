using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ItemCrudTests(ApiFactory api)
{
    [Fact]
    public async Task Seller_bisa_memasang_listing()
    {
        var seller = await api.VerifiedSellerAsync();

        var item = await api.CreateItemAsync(seller.Client, title: "Tenda Dome Kapasitas 4");

        Assert.Equal("Tenda Dome Kapasitas 4", item.Title);
        Assert.Equal(ItemStatuses.Active, item.Status);
        Assert.Empty(item.Photos);
    }

    [Fact]
    public async Task Pemilik_listing_diambil_dari_token()
    {
        var seller = await api.VerifiedSellerAsync();

        var response = await seller.Client.PostAsJsonAsync("/items", new
        {
            title         = "Proyektor Epson",
            category      = "Elektronik",
            price         = 100_000m,
            priceUnit     = PriceUnits.Day,
            depositAmount = 250_000m,

            sellerId = Guid.NewGuid(),
            status   = ItemStatuses.Inactive
        });

        response.EnsureSuccessStatusCode();
        var item = (await response.Content.ReadFromJsonAsync<ItemDetailResponse>())!;

        Assert.Equal(seller.Id, item.SellerId);
        Assert.Equal(ItemStatuses.Active, item.Status);
    }

    [Fact]
    public async Task Seller_bisa_mengubah_listingnya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var response = await seller.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = "Kamera Mirrorless + Lensa 50mm",
            Category      = "Elektronik",
            Description   = "Sudah termasuk tas dan tripod.",
            Price         = 175_000m,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = 600_000m,
            Status        = ItemStatuses.Active
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ItemDetailResponse>())!;

        Assert.Equal("Kamera Mirrorless + Lensa 50mm", updated.Title);
        Assert.Equal(175_000m, updated.Price);
        Assert.Equal(600_000m, updated.DepositAmount);
    }

    [Fact]
    public async Task Seller_lain_tidak_bisa_mengubah_listing_orang()
    {
        var pemilik = await api.VerifiedSellerAsync();
        var penyusup = await api.VerifiedSellerAsync();

        var item = await api.CreateItemAsync(pemilik.Client);

        var response = await penyusup.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = "Diambil alih",
            Category      = "Elektronik",
            Price         = 1m,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = 0m,
            Status        = ItemStatuses.Active
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var tetap = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");
        Assert.Equal(item.Title, tetap!.Title);
        Assert.Equal(item.Price, tetap.Price);
    }

    [Theory]
    [InlineData("hari")]
    [InlineData("HOUR")]
    [InlineData("")]
    public async Task Satuan_harga_ngawur_ditolak(string unit)
    {
        var seller = await api.VerifiedSellerAsync();

        var response = await seller.Client.PostAsJsonAsync("/items", new CreateItemRequest
        {
            Title         = "Barang Uji",
            Category      = "Lainnya",
            Price         = 10_000m,
            PriceUnit     = unit,
            DepositAmount = 0m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Harga_nol_atau_negatif_ditolak(decimal price)
    {
        var seller = await api.VerifiedSellerAsync();

        var response = await seller.Client.PostAsJsonAsync("/items", new CreateItemRequest
        {
            Title         = "Barang Uji",
            Category      = "Lainnya",
            Price         = price,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = 0m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deposit_negatif_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();

        var response = await seller.Client.PostAsJsonAsync("/items", new CreateItemRequest
        {
            Title         = "Barang Uji",
            Category      = "Lainnya",
            Price         = 10_000m,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = -1m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Items_mine_hanya_berisi_listing_sendiri()
    {
        var seller = await api.VerifiedSellerAsync();
        var lain = await api.VerifiedSellerAsync();

        var punyaku = await api.CreateItemAsync(seller.Client, title: "Punya Saya");
        var punyaOrang = await api.CreateItemAsync(lain.Client, title: "Punya Orang");

        var daftar = await seller.Client.GetFromJsonAsync<List<ItemSummaryResponse>>("/items/mine");

        Assert.Contains(daftar!, i => i.Id == punyaku.Id);
        Assert.DoesNotContain(daftar!, i => i.Id == punyaOrang.Id);
    }

    [Fact]
    public async Task Listing_seller_belum_terverifikasi_tidak_muncul_di_katalog()
    {
        var seller = await api.SellerAsync();
        var item = await api.CreateItemAsync(seller.Client, title: "Drone Belum Terverifikasi");

        var sebelum = await api.CreateClient()
            .GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>("/items?q=Drone");

        Assert.DoesNotContain(sebelum!.Items, i => i.Id == item.Id);

        await api.VerifySellerAsync(seller.Id);

        var sesudah = await api.CreateClient()
            .GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>("/items?q=Drone");

        Assert.Contains(sesudah!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Detail_listing_belum_terverifikasi_404_untuk_publik_tapi_terbuka_bagi_pemiliknya()
    {
        var seller = await api.SellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var publik = await api.CreateClient().GetAsync($"/items/{item.Id}");
        var pemilik = await seller.Client.GetAsync($"/items/{item.Id}");

        Assert.Equal(HttpStatusCode.NotFound, publik.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pemilik.StatusCode);
    }

    [Fact]
    public async Task Listing_inactive_hilang_dari_katalog_tapi_masih_terlihat_pemiliknya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client, title: "Gitar Akustik Yamaha");

        var nonaktif = await seller.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = item.Title,
            Category      = item.Category,
            Price         = item.Price,
            PriceUnit     = item.PriceUnit,
            DepositAmount = item.DepositAmount,
            Status        = ItemStatuses.Inactive
        });

        nonaktif.EnsureSuccessStatusCode();

        var katalog = await api.CreateClient()
            .GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>("/items?q=Gitar");

        Assert.DoesNotContain(katalog!.Items, i => i.Id == item.Id);

        var punyaSaya = await seller.Client.GetFromJsonAsync<List<ItemSummaryResponse>>("/items/mine");
        Assert.Contains(punyaSaya!, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Admin_bisa_melihat_listing_yang_belum_tampil_di_katalog()
    {
        var seller = await api.SellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var admin = await api.ClientAsAdminAsync();
        var response = await admin.GetAsync($"/items/{item.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_barang_tidak_ada_404()
    {
        var response = await api.CreateClient().GetAsync($"/items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
