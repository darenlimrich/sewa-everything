using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ItemModerationTests(ApiFactory api)
{
    private const string Alasan = "Foto tidak sesuai barang yang disewakan.";

    [Fact]
    public async Task Listing_yang_diturunkan_hilang_dari_katalog_publik()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Tenda Dome Moderasi");
        var admin  = await api.ClientAsAdminAsync();

        var sebelum = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            "/items?q=Tenda Dome Moderasi");
        Assert.Contains(sebelum!.Items, i => i.Id == item.Id);

        var turun = await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });
        Assert.Equal(HttpStatusCode.OK, turun.StatusCode);

        var sesudah = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            "/items?q=Tenda Dome Moderasi");
        Assert.DoesNotContain(sesudah!.Items, i => i.Id == item.Id);

        var detail = await api.CreateClient().GetAsync($"/items/{item.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
    }

    [Fact]
    public async Task Pemilik_tidak_bisa_menayangkan_ulang_listing_yang_diturunkan()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Proyektor Moderasi");
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });

        var ubah = await seller.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = item.Title,
            Category      = item.Category,
            Description   = item.Description,
            Price         = item.Price,
            PriceUnit     = item.PriceUnit,
            DepositAmount = item.DepositAmount,
            Status        = ItemStatuses.Active
        });
        Assert.Equal(HttpStatusCode.OK, ubah.StatusCode);

        var katalog = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            "/items?q=Proyektor Moderasi");
        Assert.DoesNotContain(katalog!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Listing_yang_diturunkan_tidak_bisa_dipesan()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var renter = await api.RenterAsync();
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });

        var mulai = DateTimeOffset.UtcNow.AddDays(3);
        var pesan = await renter.Client.PostAsJsonAsync("/bookings", new CreateBookingRequest
        {
            ItemId   = item.Id,
            StartsAt = mulai.UtcDateTime,
            EndsAt   = mulai.AddDays(2).UtcDateTime
        });

        Assert.Equal(HttpStatusCode.NotFound, pesan.StatusCode);
    }

    [Fact]
    public async Task Pemilik_melihat_alasan_penurunannya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });

        var detail = await seller.Client.GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");

        Assert.NotNull(detail!.SuspendedAt);
        Assert.Equal(Alasan, detail.SuspensionReason);

        var milikku = await seller.Client.GetFromJsonAsync<List<ItemSummaryResponse>>("/items/mine");
        Assert.NotNull(milikku!.Single(i => i.Id == item.Id).SuspendedAt);
    }

    [Fact]
    public async Task Memulihkan_mengembalikan_listing_ke_katalog()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Drone Pulih");
        var admin  = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });

        var pulih = await admin.PostAsync($"/admin/items/{item.Id}/unsuspend", null);
        Assert.Equal(HttpStatusCode.OK, pulih.StatusCode);

        var dipulihkan = await pulih.Content.ReadFromJsonAsync<ModeratedItemResponse>();
        Assert.Null(dipulihkan!.SuspendedAt);
        Assert.True(dipulihkan.IsPubliclyVisible);

        var katalog = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            "/items?q=Drone Pulih");
        Assert.Contains(katalog!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Memulihkan_tidak_menyalakan_listing_yang_dimatikan_pemiliknya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Kayak Nonaktif");
        var admin  = await api.ClientAsAdminAsync();

        await seller.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = item.Title,
            Category      = item.Category,
            Description   = item.Description,
            Price         = item.Price,
            PriceUnit     = item.PriceUnit,
            DepositAmount = item.DepositAmount,
            Status        = ItemStatuses.Inactive
        });

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });
        await admin.PostAsync($"/admin/items/{item.Id}/unsuspend", null);

        var sesudah = await seller.Client.GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");
        Assert.Equal(ItemStatuses.Inactive, sesudah!.Status);

        var katalog = await api.CreateClient().GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            "/items?q=Kayak Nonaktif");
        Assert.DoesNotContain(katalog!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Penurunan_tanpa_alasan_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var admin  = await api.ClientAsAdminAsync();

        var kosong = await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, kosong.StatusCode);
    }

    [Fact]
    public async Task Seller_dan_renter_tidak_bisa_menurunkan_listing()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var renter = await api.RenterAsync();

        var olehSeller = await seller.Client.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });
        Assert.Equal(HttpStatusCode.Forbidden, olehSeller.StatusCode);

        var olehRenter = await renter.Client.PostAsJsonAsync(
            $"/admin/items/{item.Id}/suspend", new SuspendItemRequest { Reason = Alasan });
        Assert.Equal(HttpStatusCode.Forbidden, olehRenter.StatusCode);

        var daftar = await renter.Client.GetAsync("/admin/items");
        Assert.Equal(HttpStatusCode.Forbidden, daftar.StatusCode);
    }

    [Fact]
    public async Task Daftar_moderasi_memuat_listing_yang_tidak_tampil_publik()
    {
        var seller = await api.SellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Sepeda Belum Verif");
        var admin  = await api.ClientAsAdminAsync();

        var daftar = await admin.GetFromJsonAsync<PagedResponse<ModeratedItemResponse>>(
            "/admin/items?q=Sepeda Belum Verif");

        var baris = Assert.Single(daftar!.Items, i => i.Id == item.Id);
        Assert.False(baris.SellerIsVerified);
        Assert.False(baris.IsPubliclyVisible);
        Assert.Null(baris.SuspendedAt);
    }

    [Fact]
    public async Task Daftar_moderasi_bisa_disaring_yang_sedang_diturunkan()
    {
        var seller    = await api.VerifiedSellerAsync();
        var diturunkan = await api.CreateItemAsync(seller.Client, title: "Gitar Turun");
        var biasa      = await api.CreateItemAsync(seller.Client, title: "Gitar Biasa");
        var admin      = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{diturunkan.Id}/suspend", new SuspendItemRequest { Reason = Alasan });

        var hanyaTurun = await admin.GetFromJsonAsync<PagedResponse<ModeratedItemResponse>>(
            "/admin/items?suspended=true&pageSize=100");

        Assert.Contains(hanyaTurun!.Items, i => i.Id == diturunkan.Id);
        Assert.DoesNotContain(hanyaTurun.Items, i => i.Id == biasa.Id);

        var hanyaHidup = await admin.GetFromJsonAsync<PagedResponse<ModeratedItemResponse>>(
            "/admin/items?suspended=false&pageSize=100");

        Assert.Contains(hanyaHidup!.Items, i => i.Id == biasa.Id);
        Assert.DoesNotContain(hanyaHidup.Items, i => i.Id == diturunkan.Id);
    }

    [Fact]
    public async Task Menurunkan_listing_tidak_membatalkan_sewa_yang_sudah_jalan()
    {
        var mulai    = DateTimeOffset.UtcNow.AddDays(5);
        var skenario = await api.ActiveBookingAsync(mulai, mulai.AddDays(2));
        var admin    = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync(
            $"/admin/items/{skenario.Booking.ItemId}/suspend", new SuspendItemRequest { Reason = Alasan });

        Assert.Equal("active", await api.StatusOfAsync(skenario.Booking.Id));
    }
}
