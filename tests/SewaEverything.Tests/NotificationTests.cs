using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory api)
{
    private static readonly DateTimeOffset Besok = DateTimeOffset.UtcNow.Date.AddDays(1);

    [Fact]
    public async Task Akun_tanpa_sewa_tidak_punya_notifikasi()
    {
        var renter = await api.RenterAsync();

        var list = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.Empty(list!.Items);
        Assert.Equal(0, list.UnreadCount);
        Assert.Null(list.SeenAt);
    }

    [Fact]
    public async Task Sewa_baru_muncul_untuk_KEDUA_pihak_dengan_sudut_pandang_masing_masing()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(
            renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var punyaPenyewa = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        var punyaPemilik = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        var n1 = Assert.Single(punyaPenyewa!.Items);
        var n2 = Assert.Single(punyaPemilik!.Items, n => n.Kind == NotificationKinds.Seller);

        Assert.Equal(booking.Id, n1.BookingId);
        Assert.Equal(NotificationKinds.Renter, n1.Kind);
        Assert.Equal(booking.RenterTotal, n1.Amount);

        Assert.Equal(booking.Id, n2.BookingId);
        Assert.Equal(NotificationKinds.Seller, n2.Kind);
        Assert.Equal(booking.SellerGross, n2.Amount);
    }

    [Fact]
    public async Task Statusnya_ikut_bergerak_saat_sewa_disetujui()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(
            renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var sebelum = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.Equal("pending", sebelum!.Items[0].Status);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var sesudah = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.Equal("confirmed", sesudah!.Items[0].Status);
    }

    [Fact]
    public async Task Belum_pernah_dilihat_berarti_semuanya_belum_terbaca()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var list = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.Equal(1, list!.UnreadCount);
        Assert.True(list.Items[0].Unread);
    }

    [Fact]
    public async Task Menandai_terbaca_menihilkan_penghitungnya()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var tandai = await renter.Client.PostAsync("/notifications/seen", null);
        Assert.Equal(HttpStatusCode.NoContent, tandai.StatusCode);

        var list = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.Equal(0, list!.UnreadCount);
        Assert.False(list.Items[0].Unread);
        Assert.NotNull(list.SeenAt);
    }

    [Fact]
    public async Task Perubahan_SETELAH_ditandai_terbaca_menjadi_belum_terbaca_lagi()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);
        var booking = await api.CreateBookingAsync(
            renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        (await renter.Client.PostAsync("/notifications/seen", null)).EnsureSuccessStatusCode();

        var tenang = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.Equal(0, tenang!.UnreadCount);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var lagi = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.Equal(1, lagi!.UnreadCount);
        Assert.True(lagi.Items[0].Unread);
    }

    [Fact]
    public async Task Notifikasi_orang_lain_tidak_bocor()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renterA = await api.RenterAsync();
        var renterB = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renterA.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var punyaB = await renterB.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.Empty(punyaB!.Items);
    }

    [Fact]
    public async Task Menandai_terbaca_hanya_menyentuh_akun_sendiri()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        (await renter.Client.PostAsync("/notifications/seen", null)).EnsureSuccessStatusCode();

        var punyaPemilik = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.Single(punyaPemilik!.Items, n => n.Kind == NotificationKinds.Seller && n.Unread);
    }

    [Fact]
    public async Task Yang_terbaru_di_atas()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item1  = await api.CreateItemAsync(seller.Client, title: "Barang Pertama");
        var item2  = await api.CreateItemAsync(seller.Client, title: "Barang Kedua");

        await api.CreateBookingAsync(renter.Client, item1.Id, Besok.AddDays(1), Besok.AddDays(3));
        await api.CreateBookingAsync(renter.Client, item2.Id, Besok.AddDays(5), Besok.AddDays(7));

        var list = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.Equal(2, list!.Items.Count);
        Assert.True(list.Items[0].At >= list.Items[1].At);
    }

    [Fact]
    public async Task Keputusan_admin_atas_listing_muncul_di_notifikasi_pemilik()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Tenda Notif Listing", approve: false);
        var admin  = await api.ClientAsAdminAsync();

        var sebelum = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.DoesNotContain(sebelum!.Items, n => n.Kind == NotificationKinds.Listing && n.ItemId == item.Id);

        (await seller.Client.PostAsync("/notifications/seen", null)).EnsureSuccessStatusCode();

        await admin.PostAsJsonAsync(
            $"/admin/items/{item.Id}/reject", new RejectItemRequest { Reason = "Foto belum menunjukkan barangnya." });

        var ditolak = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        var n = Assert.Single(ditolak!.Items, n => n.Kind == NotificationKinds.Listing && n.ItemId == item.Id);
        Assert.Equal(ItemReviewStatuses.Rejected, n.Status);
        Assert.Equal("Tenda Notif Listing", n.ItemTitle);
        Assert.Null(n.BookingId);
        Assert.Null(n.Amount);
        Assert.True(n.Unread);
        Assert.Equal(1, ditolak.UnreadCount);

        (await admin.PostAsync($"/admin/items/{item.Id}/approve", null)).EnsureSuccessStatusCode();

        var disetujui = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        var m = Assert.Single(disetujui!.Items, n => n.Kind == NotificationKinds.Listing && n.ItemId == item.Id);
        Assert.Equal(ItemReviewStatuses.Approved, m.Status);
        Assert.True(m.At >= n.At);
    }

    [Fact]
    public async Task Listing_yang_ditarik_kembali_ke_antrean_hilang_dari_notifikasi()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, title: "Speaker Notif Ulang");

        var sebelum = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.Contains(sebelum!.Items, n => n.Kind == NotificationKinds.Listing && n.ItemId == item.Id);

        (await seller.Client.PutAsJsonAsync($"/items/{item.Id}", new UpdateItemRequest
        {
            Title         = "Speaker Notif Ulang Pro",
            Category      = item.Category,
            Description   = item.Description,
            Price         = item.Price,
            PriceUnit     = item.PriceUnit,
            DepositAmount = item.DepositAmount,
            DeliveryFee   = item.DeliveryFee,
            Status        = item.Status
        })).EnsureSuccessStatusCode();

        var sesudah = await seller.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");
        Assert.DoesNotContain(sesudah!.Items, n => n.Kind == NotificationKinds.Listing && n.ItemId == item.Id);
    }

    [Fact]
    public async Task Denyut_pemilik_naik_saat_listingnya_diputus_dan_turun_setelah_dilihat()
    {
        var seller = await api.VerifiedSellerAsync();
        var item   = await api.CreateItemAsync(seller.Client, approve: false);
        var admin  = await api.ClientAsAdminAsync();

        (await seller.Client.PostAsync("/notifications/seen", null)).EnsureSuccessStatusCode();

        var tenang = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(0, tenang!.Unread);

        (await admin.PostAsync($"/admin/items/{item.Id}/approve", null)).EnsureSuccessStatusCode();

        var naik = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(1, naik!.Unread);

        (await seller.Client.PostAsync("/notifications/seen", null)).EnsureSuccessStatusCode();

        var turun = await seller.Client.GetFromJsonAsync<PulseResponse>("/pulse");
        Assert.Equal(0, turun!.Unread);
    }

    [Fact]
    public async Task Penyewa_tidak_menerima_notifikasi_listing()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);

        await api.CreateBookingAsync(renter.Client, item.Id, Besok.AddDays(1), Besok.AddDays(4));

        var punyaPenyewa = await renter.Client.GetFromJsonAsync<NotificationListResponse>("/notifications");

        Assert.DoesNotContain(punyaPenyewa!.Items, n => n.Kind == NotificationKinds.Listing);
    }

    [Fact]
    public async Task Tanpa_token_ditolak()
    {
        var response = await api.CreateClient().GetAsync("/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
