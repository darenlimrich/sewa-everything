using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ItemBlackoutTests(ApiFactory api)
{
    private static Task<HttpResponseMessage> BlockAsync(
        HttpClient client, Guid itemId, DateTimeOffset from, DateTimeOffset to, string? reason = null) =>
        client.PostAsJsonAsync($"/items/{itemId}/blackouts", new CreateBlackoutRequest
        {
            StartsAt = from,
            EndsAt   = to,
            Reason   = reason
        });

    [Fact]
    public async Task Seller_bisa_memblok_tanggal()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(5);
        var selesai = mulai.AddDays(3);

        var response = await BlockAsync(seller.Client, item.Id, mulai, selesai, "Diservis");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var blackout = (await response.Content.ReadFromJsonAsync<ItemBlackoutResponse>())!;

        Assert.Equal("Diservis", blackout.Reason);
        Assert.Equal(mulai.UtcDateTime, blackout.StartsAt, TimeSpan.FromSeconds(1));
        Assert.Equal(selesai.UtcDateTime, blackout.EndsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Blackout_tumpang_tindih_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(40);

        var pertama = await BlockAsync(seller.Client, item.Id, mulai, mulai.AddDays(5));
        var kedua = await BlockAsync(seller.Client, item.Id, mulai.AddDays(2), mulai.AddDays(7));

        Assert.Equal(HttpStatusCode.Created, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Fact]
    public async Task Blackout_bersambungan_diterima()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(50);
        var batas = mulai.AddDays(3);

        var pertama = await BlockAsync(seller.Client, item.Id, mulai, batas);
        var kedua = await BlockAsync(seller.Client, item.Id, batas, batas.AddDays(3));

        Assert.Equal(HttpStatusCode.Created, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Created, kedua.StatusCode);
    }

    [Fact]
    public async Task Blackout_di_atas_booking_hidup_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RegisterAsync(Roles.Renter);
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(60);
        var selesai = mulai.AddDays(2);

        await api.SeedBookingAsync(item.Id, renter.User.Id, mulai, selesai);

        var response = await BlockAsync(seller.Client, item.Id, mulai.AddDays(1), selesai.AddDays(1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemShape>();
        Assert.Contains("dipesan", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rentang_terbalik_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var waktu = DateTimeOffset.UtcNow.AddDays(70);

        var response = await BlockAsync(seller.Client, item.Id, waktu.AddDays(3), waktu);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rentang_kosong_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var waktu = DateTimeOffset.UtcNow.AddDays(75);

        var response = await BlockAsync(seller.Client, item.Id, waktu, waktu);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Seller_lain_tidak_bisa_memblok_barang_orang()
    {
        var pemilik = await api.VerifiedSellerAsync();
        var penyusup = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(pemilik.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(80);

        var response = await BlockAsync(penyusup.Client, item.Id, mulai, mulai.AddDays(1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Blackout_muncul_di_kalender_detail_barang()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(15);
        var selesai = mulai.AddDays(2);

        await BlockAsync(seller.Client, item.Id, mulai, selesai);

        var detail = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");

        var blok = Assert.Single(detail!.BlockedRanges);
        Assert.Equal(BlockedRangeSources.Blackout, blok.Source);
        Assert.Equal(mulai.UtcDateTime, blok.StartsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Kalender_menampilkan_booking_dan_blackout_sekaligus()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RegisterAsync(Roles.Renter);
        var item = await api.CreateItemAsync(seller.Client);

        var awal = DateTimeOffset.UtcNow.AddDays(3);

        await api.SeedBookingAsync(item.Id, renter.User.Id, awal, awal.AddDays(2));
        await BlockAsync(seller.Client, item.Id, awal.AddDays(10), awal.AddDays(12));

        var detail = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");

        Assert.Equal(2, detail!.BlockedRanges.Count);
        Assert.Contains(detail.BlockedRanges, r => r.Source == BlockedRangeSources.Booking);
        Assert.Contains(detail.BlockedRanges, r => r.Source == BlockedRangeSources.Blackout);

        Assert.Equal(
            detail.BlockedRanges.OrderBy(r => r.StartsAt).Select(r => r.StartsAt),
            detail.BlockedRanges.Select(r => r.StartsAt));
    }

    [Fact]
    public async Task Kalender_tidak_mengambil_rentang_di_luar_jendela_yang_diminta()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var jauh = DateTimeOffset.UtcNow.AddDays(200);
        await BlockAsync(seller.Client, item.Id, jauh, jauh.AddDays(2));

        var bawaan = await api.CreateClient()
            .GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");

        var diperlebar = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>(
            $"/items/{item.Id}?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(300).ToString("O"))}");

        Assert.Empty(bawaan!.BlockedRanges);
        Assert.Single(diperlebar!.BlockedRanges);
    }

    [Fact]
    public async Task Jendela_kalender_terlalu_lebar_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var response = await api.CreateClient().GetAsync(
            $"/items/{item.Id}?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(400).ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Blackout_mengeluarkan_barang_dari_hasil_pencarian_tanggal_itu()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Blackout-{Guid.NewGuid():N}";

        var diblok = await api.CreateItemAsync(seller.Client, category: kategori);
        var bebas = await api.CreateItemAsync(seller.Client, category: kategori);

        var mulai = DateTimeOffset.UtcNow.AddDays(25);
        var selesai = mulai.AddDays(2);

        await BlockAsync(seller.Client, diblok.Id, mulai, selesai);

        var hasil = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            $"/items?category={kategori}" +
            $"&availableFrom={Uri.EscapeDataString(mulai.ToString("O"))}" +
            $"&availableTo={Uri.EscapeDataString(selesai.ToString("O"))}");

        Assert.Single(hasil!.Items);
        Assert.Equal(bebas.Id, hasil.Items[0].Id);
    }

    [Fact]
    public async Task Hapus_blackout_membebaskan_kembali_tanggalnya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(35);

        var dibuat = await BlockAsync(seller.Client, item.Id, mulai, mulai.AddDays(2));
        var blackout = (await dibuat.Content.ReadFromJsonAsync<ItemBlackoutResponse>())!;

        var hapus = await seller.Client.DeleteAsync($"/items/{item.Id}/blackouts/{blackout.Id}");
        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);

        var detail = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");
        Assert.Empty(detail!.BlockedRanges);
    }

    [Fact]
    public async Task Daftar_blackout_hanya_untuk_pemiliknya()
    {
        var pemilik = await api.VerifiedSellerAsync();
        var penyusup = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(pemilik.Client);

        var mulai = DateTimeOffset.UtcNow.AddDays(90);
        await BlockAsync(pemilik.Client, item.Id, mulai, mulai.AddDays(1));

        var punyaPemilik = await pemilik.Client.GetAsync($"/items/{item.Id}/blackouts");
        var punyaPenyusup = await penyusup.Client.GetAsync($"/items/{item.Id}/blackouts");

        Assert.Equal(HttpStatusCode.OK, punyaPemilik.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, punyaPenyusup.StatusCode);

        var daftar = (await punyaPemilik.Content.ReadFromJsonAsync<List<ItemBlackoutResponse>>())!;
        Assert.Single(daftar);
    }

    private sealed record ProblemShape(string? Title, string? Detail);
}
