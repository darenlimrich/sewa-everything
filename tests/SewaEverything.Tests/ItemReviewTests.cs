using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ItemReviewTests(ApiFactory api)
{
    private const string Alasan = "Foto belum menunjukkan kondisi barang yang sebenarnya.";

    private static UpdateItemRequest Ubah(ItemDetailResponse item,
        string? title = null, decimal? price = null, string? description = null, string? status = null) => new()
    {
        Title         = title ?? item.Title,
        Category      = item.Category,
        Description   = description ?? item.Description,
        Price         = price ?? item.Price,
        PriceUnit     = item.PriceUnit,
        DepositAmount = item.DepositAmount,
        DeliveryFee   = item.DeliveryFee,
        Status        = status ?? item.Status
    };

    private async Task<bool> TampilDiKatalogAsync(string judul, Guid id)
    {
        var hasil = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            $"/items?q={Uri.EscapeDataString(judul)}");

        return hasil!.Items.Any(i => i.Id == id);
    }

    [Fact]
    public async Task Listing_baru_lahir_menunggu_dan_tidak_tampil_di_katalog()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Tenda Tinjau Baru", approve: false);

        Assert.Equal(ItemReviewStatuses.Pending, item.ReviewStatus);
        Assert.False(await TampilDiKatalogAsync("Tenda Tinjau Baru", item.Id));

        var publik = await api.CreateClient().GetAsync($"/items/{item.Id}");
        Assert.Equal(HttpStatusCode.NotFound, publik.StatusCode);

        var pemilik = await seller.Client.GetAsync($"/items/{item.Id}");
        Assert.Equal(HttpStatusCode.OK, pemilik.StatusCode);

        var admin = await api.ClientAsAdminAsync();
        var staf  = await admin.GetAsync($"/items/{item.Id}");
        Assert.Equal(HttpStatusCode.OK, staf.StatusCode);
    }

    [Fact]
    public async Task Admin_menyetujui_dan_listing_tampil()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Proyektor Tinjau Setuju", approve: false);
        var admin  = await api.ClientAsAdminAsync();

        var setuju = await admin.PostAsync($"/admin/items/{item.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, setuju.StatusCode);

        var hasil = await setuju.Content.ReadFromJsonAsync<ModeratedItemResponse>();
        Assert.Equal(ItemReviewStatuses.Approved, hasil!.ReviewStatus);
        Assert.NotNull(hasil.ReviewedAt);
        Assert.Equal("Admin Test", hasil.ReviewedByName);
        Assert.True(hasil.IsPubliclyVisible);

        Assert.True(await TampilDiKatalogAsync("Proyektor Tinjau Setuju", item.Id));

        var publik = await api.CreateClient().GetAsync($"/items/{item.Id}");
        Assert.Equal(HttpStatusCode.OK, publik.StatusCode);
    }

    [Fact]
    public async Task Menyetujui_dua_kali_tidak_mengubah_apa_pun()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var admin  = await api.ClientAsAdminAsync();

        var pertama = await (await admin.PostAsync($"/admin/items/{item.Id}/approve", null))
            .Content.ReadFromJsonAsync<ModeratedItemResponse>();
        var kedua = await admin.PostAsync($"/admin/items/{item.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, kedua.StatusCode);
        var hasil = await kedua.Content.ReadFromJsonAsync<ModeratedItemResponse>();
        Assert.Equal(pertama!.ReviewedAt!.Value, hasil!.ReviewedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Admin_menolak_dengan_alasan_dan_pemilik_membacanya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Drone Tinjau Tolak", approve: false);
        var admin  = await api.ClientAsAdminAsync();

        var tolak = await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });
        Assert.Equal(HttpStatusCode.OK, tolak.StatusCode);

        var hasil = await tolak.Content.ReadFromJsonAsync<ModeratedItemResponse>();
        Assert.Equal(ItemReviewStatuses.Rejected, hasil!.ReviewStatus);
        Assert.Equal(Alasan, hasil.RejectionReason);
        Assert.False(hasil.IsPubliclyVisible);

        Assert.False(await TampilDiKatalogAsync("Drone Tinjau Tolak", item.Id));

        var detail = await seller.Client.GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");
        Assert.Equal(ItemReviewStatuses.Rejected, detail!.ReviewStatus);
        Assert.Equal(Alasan, detail.RejectionReason);

        var milikku = await seller.Client.GetFromJsonAsync<List<ItemSummaryResponse>>("/items/mine");
        var baris = milikku!.Single(i => i.Id == item.Id);
        Assert.Equal(ItemReviewStatuses.Rejected, baris.ReviewStatus);
        Assert.Equal(Alasan, baris.RejectionReason);
    }

    [Fact]
    public async Task Menolak_tanpa_alasan_ditolak_400()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var admin  = await api.ClientAsAdminAsync();

        var kosong = await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, kosong.StatusCode);

        var pendek = await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = "no" });
        Assert.Equal(HttpStatusCode.BadRequest, pendek.StatusCode);

        Assert.Equal(ItemReviewStatuses.Pending, await api.ItemReviewStatusAsync(item.Id));
    }

    [Fact]
    public async Task Listing_yang_sudah_disetujui_tidak_dapat_ditolak_melainkan_diturunkan()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var admin  = await api.ClientAsAdminAsync();

        var tolak = await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });

        Assert.Equal(HttpStatusCode.Conflict, tolak.StatusCode);
        Assert.Equal(ItemReviewStatuses.Approved, await api.ItemReviewStatusAsync(item.Id));
    }

    [Fact]
    public async Task Admin_dapat_menyetujui_listing_yang_terlanjur_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Kamera Tinjau Balik", approve: false);
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });

        var setuju = await admin.PostAsync($"/admin/items/{item.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, setuju.StatusCode);

        var hasil = await setuju.Content.ReadFromJsonAsync<ModeratedItemResponse>();
        Assert.Equal(ItemReviewStatuses.Approved, hasil!.ReviewStatus);
        Assert.Null(hasil.RejectionReason);
        Assert.True(await TampilDiKatalogAsync("Kamera Tinjau Balik", item.Id));
    }

    [Theory]
    [InlineData(Roles.Seller)]
    [InlineData(Roles.Renter)]
    public async Task Bukan_admin_tidak_dapat_menyetujui_maupun_menolak(string role)
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var client = await api.ClientAsAsync(role);

        var setuju = await client.PostAsync($"/admin/items/{item.Id}/approve", null);
        var tolak  = await client.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });

        Assert.Equal(HttpStatusCode.Forbidden, setuju.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, tolak.StatusCode);
        Assert.Equal(ItemReviewStatuses.Pending, await api.ItemReviewStatusAsync(item.Id));
    }

    [Fact]
    public async Task Owner_tidak_dapat_menyetujui()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var owner  = await api.ClientAsOwnerAsync();

        var setuju = await owner.PostAsync($"/admin/items/{item.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, setuju.StatusCode);
    }

    [Fact]
    public async Task Mengubah_judul_listing_yang_disetujui_mengirimnya_ditinjau_ulang()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Speaker Tinjau Ulang");

        Assert.True(await TampilDiKatalogAsync("Speaker Tinjau Ulang", item.Id));

        var ubah = await seller.Client.PutAsJsonAsync(
            $"/items/{item.Id}", Ubah(item, title: "Speaker Tinjau Ulang Pro"));
        Assert.Equal(HttpStatusCode.OK, ubah.StatusCode);

        var hasil = await ubah.Content.ReadFromJsonAsync<ItemDetailResponse>();
        Assert.Equal(ItemReviewStatuses.Pending, hasil!.ReviewStatus);
        Assert.Null(hasil.ReviewedAt);

        Assert.False(await TampilDiKatalogAsync("Speaker Tinjau Ulang", item.Id));
    }

    [Fact]
    public async Task Mengubah_harga_listing_yang_disetujui_tidak_menyentuh_persetujuannya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Tripod Tinjau Harga");

        var ubah = await seller.Client.PutAsJsonAsync(
            $"/items/{item.Id}", Ubah(item, price: item.Price + 10_000m, status: ItemStatuses.Inactive));
        Assert.Equal(HttpStatusCode.OK, ubah.StatusCode);

        var hasil = await ubah.Content.ReadFromJsonAsync<ItemDetailResponse>();
        Assert.Equal(ItemReviewStatuses.Approved, hasil!.ReviewStatus);

        var nyalakan = await seller.Client.PutAsJsonAsync(
            $"/items/{item.Id}", Ubah(item, price: item.Price + 10_000m, status: ItemStatuses.Active));
        Assert.Equal(HttpStatusCode.OK, nyalakan.StatusCode);
        Assert.True(await TampilDiKatalogAsync("Tripod Tinjau Harga", item.Id));
    }

    [Fact]
    public async Task Menyimpan_tanpa_perubahan_tidak_mengirim_ulang()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Mixer Tinjau Sama");

        var ubah = await seller.Client.PutAsJsonAsync($"/items/{item.Id}", Ubah(item));
        Assert.Equal(HttpStatusCode.OK, ubah.StatusCode);

        var hasil = await ubah.Content.ReadFromJsonAsync<ItemDetailResponse>();
        Assert.Equal(ItemReviewStatuses.Approved, hasil!.ReviewStatus);
    }

    [Fact]
    public async Task Menambah_foto_ke_listing_yang_disetujui_mengirimnya_ditinjau_ulang()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Lensa Tinjau Foto");

        await ItemPhotoTests.UploadAsync(seller.Client, item.Id, ItemPhotoTests.Png());

        Assert.Equal(ItemReviewStatuses.Pending, await api.ItemReviewStatusAsync(item.Id));
        Assert.False(await TampilDiKatalogAsync("Lensa Tinjau Foto", item.Id));
    }

    [Fact]
    public async Task Menghapus_foto_dari_listing_yang_disetujui_tidak_menyentuh_persetujuannya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var foto   = await ItemPhotoTests.UploadAsync(seller.Client, item.Id, ItemPhotoTests.Png());
        await api.ApproveItemAsync(item.Id);

        var hapus = await seller.Client.DeleteAsync($"/items/{item.Id}/photos/{foto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);

        Assert.Equal(ItemReviewStatuses.Approved, await api.ItemReviewStatusAsync(item.Id));
    }

    [Fact]
    public async Task Listing_yang_ditolak_otomatis_diajukan_ulang_saat_diperbaiki()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });

        var ubah = await seller.Client.PutAsJsonAsync(
            $"/items/{item.Id}", Ubah(item, price: item.Price - 5_000m));
        Assert.Equal(HttpStatusCode.OK, ubah.StatusCode);

        var hasil = await ubah.Content.ReadFromJsonAsync<ItemDetailResponse>();
        Assert.Equal(ItemReviewStatuses.Pending, hasil!.ReviewStatus);
        Assert.Null(hasil.RejectionReason);
    }

    [Fact]
    public async Task Listing_yang_ditolak_tetap_ditolak_kalau_hanya_status_yang_disentuh()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });

        var ubah = await seller.Client.PutAsJsonAsync(
            $"/items/{item.Id}", Ubah(item, status: ItemStatuses.Inactive));
        Assert.Equal(HttpStatusCode.OK, ubah.StatusCode);

        var hasil = await ubah.Content.ReadFromJsonAsync<ItemDetailResponse>();
        Assert.Equal(ItemReviewStatuses.Rejected, hasil!.ReviewStatus);
        Assert.Equal(Alasan, hasil.RejectionReason);
    }

    [Fact]
    public async Task Menghapus_foto_dari_listing_yang_ditolak_mengajukannya_ulang()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var foto   = await ItemPhotoTests.UploadAsync(seller.Client, item.Id, ItemPhotoTests.Png());
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = Alasan });

        var hapus = await seller.Client.DeleteAsync($"/items/{item.Id}/photos/{foto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);

        Assert.Equal(ItemReviewStatuses.Pending, await api.ItemReviewStatusAsync(item.Id));
    }

    [Fact]
    public async Task Listing_yang_menunggu_tidak_dapat_dipesan_maupun_masuk_keranjang()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var renter = await api.RenterAsync();

        var mulai = DateTimeOffset.UtcNow.AddDays(3);
        var pesan = await api.BookAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));
        Assert.Equal(HttpStatusCode.NotFound, pesan.StatusCode);

        var keranjang = await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId  = item.Id,
            StartAt = mulai.UtcDateTime,
            EndAt   = mulai.AddDays(2).UtcDateTime
        });
        Assert.Equal(HttpStatusCode.NotFound, keranjang.StatusCode);
    }

    [Fact]
    public async Task Antrean_admin_menyaring_menurut_status_peninjauan()
    {
        var seller   = await api.VerifiedSellerAsync();
        var menunggu = await api.CreateItemAsync(seller.Client, title: "Antrean Menunggu Uji", approve: false);
        var lolos    = await api.CreateItemAsync(seller.Client, title: "Antrean Lolos Uji");
        var admin    = await api.ClientAsAdminAsync();

        var pending = await admin.GetFromJsonAsync<PagedResponse<ModeratedItemResponse>>(
            "/admin/items?review=pending&pageSize=100&q=Antrean");
        Assert.Contains(pending!.Items, i => i.Id == menunggu.Id);
        Assert.DoesNotContain(pending.Items, i => i.Id == lolos.Id);

        var approved = await admin.GetFromJsonAsync<PagedResponse<ModeratedItemResponse>>(
            "/admin/items?review=approved&pageSize=100&q=Antrean");
        Assert.Contains(approved!.Items, i => i.Id == lolos.Id);
        Assert.DoesNotContain(approved.Items, i => i.Id == menunggu.Id);

        var ngawur = await admin.GetAsync("/admin/items?review=ngawur");
        Assert.Equal(HttpStatusCode.BadRequest, ngawur.StatusCode);
    }

    [Fact]
    public async Task Denyut_admin_menghitung_listing_yang_menunggu()
    {
        var admin   = await api.ClientAsAdminAsync();
        var sebelum = await admin.GetFromJsonAsync<PulseResponse>("/pulse");

        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);

        var sesudah = await admin.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(sebelum!.PendingItems + 1, sesudah!.PendingItems);

        var penjual = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(0, penjual!.PendingItems);

        await admin.PostAsync($"/admin/items/{item.Id}/approve", null);

        var selesai = await admin.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(sebelum.PendingItems, selesai!.PendingItems);
    }
}
