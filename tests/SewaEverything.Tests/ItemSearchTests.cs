using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ItemSearchTests(ApiFactory api)
{
    private static Task<PagedResponse<ItemSummaryResponse>?> SearchAsync(HttpClient client, string query) =>
        client.GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>($"/items?{query}");

    [Fact]
    public async Task Saringan_pemilik_hanya_memulangkan_barang_pemilik_itu()
    {
        var satu = await api.VerifiedSellerAsync();
        var dua  = await api.VerifiedSellerAsync();

        var miliknya = await api.CreateItemAsync(satu.Client, title: "Tenda Dome Kapasitas Empat");
        var milikLain = await api.CreateItemAsync(dua.Client, title: "Tenda Dome Kapasitas Enam");

        var hasil = await SearchAsync(api.CreateClient(), $"sellerId={miliknya.SellerId}");

        Assert.Contains(hasil!.Items, i => i.Id == miliknya.Id);
        Assert.DoesNotContain(hasil.Items, i => i.Id == milikLain.Id);
        Assert.All(hasil.Items, i => Assert.Equal(miliknya.SellerId, i.SellerId));
    }

    [Fact]
    public async Task Saringan_pemilik_ikut_menyembunyikan_barang_yang_tidak_tampil_publik()
    {
        var seller = await api.VerifiedSellerAsync();
        var tampil = await api.CreateItemAsync(seller.Client, title: "Kompor Portabel Gas");
        var mati   = await api.CreateItemAsync(seller.Client, title: "Kompor Portabel Spiritus");

        (await seller.Client.PutAsJsonAsync($"/items/{mati.Id}", new UpdateItemRequest
        {
            Title         = mati.Title,
            Category      = mati.Category,
            Description   = mati.Description,
            Price         = mati.Price,
            PriceUnit     = mati.PriceUnit,
            DepositAmount = mati.DepositAmount,
            Status        = ItemStatuses.Inactive
        })).EnsureSuccessStatusCode();

        var hasil = await SearchAsync(api.CreateClient(), $"sellerId={seller.Id}");

        Assert.Contains(hasil!.Items, i => i.Id == tampil.Id);
        Assert.DoesNotContain(hasil.Items, i => i.Id == mati.Id);
    }

    [Fact]
    public async Task Kata_kunci_mencocokkan_judul()
    {
        var seller = await api.VerifiedSellerAsync();
        var cocok = await api.CreateItemAsync(seller.Client, title: "Sepeda Gunung Polygon");
        var tidak = await api.CreateItemAsync(seller.Client, title: "Blender Philips");

        var hasil = await SearchAsync(api.CreateClient(), "q=sepeda");

        Assert.Contains(hasil!.Items, i => i.Id == cocok.Id);
        Assert.DoesNotContain(hasil.Items, i => i.Id == tidak.Id);
    }

    [Fact]
    public async Task Kata_kunci_tidak_peduli_huruf_besar_kecil()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client, title: "Mesin Espresso Delonghi");

        var hasil = await SearchAsync(api.CreateClient(), "q=ESPRESSO");

        Assert.Contains(hasil!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Pencarian_mengenali_imbuhan_bahasa_indonesia()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client, title: "Penyewaan Panggung Portabel");

        var hasil = await SearchAsync(api.CreateClient(), "q=sewa panggung");

        Assert.Contains(hasil!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Kata_kunci_mencocokkan_deskripsi()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(
            seller.Client, title: "Paket Camping", description: "Termasuk nesting dan kompor portabel.");

        var hasil = await SearchAsync(api.CreateClient(), "q=nesting");

        Assert.Contains(hasil!.Items, i => i.Id == item.Id);
    }

    [Theory]
    [InlineData("kamera & | ! <-> :*")]
    [InlineData("\"tanda kutip menggantung")]
    [InlineData("or or or")]
    [InlineData("()")]
    [InlineData("-")]
    public async Task Kata_kunci_ngawur_tidak_meledak(string keyword)
    {
        var response = await api.CreateClient().GetAsync($"/items?q={Uri.EscapeDataString(keyword)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Filter_kategori_tidak_peduli_huruf_besar_kecil()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Alat-Musik-{Guid.NewGuid():N}";

        var item = await api.CreateItemAsync(seller.Client, title: "Keyboard Roland", category: kategori);

        var hasil = await SearchAsync(api.CreateClient(), $"category={kategori.ToLowerInvariant()}");

        Assert.Single(hasil!.Items);
        Assert.Equal(item.Id, hasil.Items[0].Id);
    }

    [Fact]
    public async Task Filter_rentang_harga()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Harga-{Guid.NewGuid():N}";

        var murah = await api.CreateItemAsync(seller.Client, category: kategori, price: 50_000m);
        var sedang = await api.CreateItemAsync(seller.Client, category: kategori, price: 150_000m);
        var mahal = await api.CreateItemAsync(seller.Client, category: kategori, price: 500_000m);

        var hasil = await SearchAsync(api.CreateClient(),
            $"category={kategori}&minPrice=100000&maxPrice=200000");

        Assert.Single(hasil!.Items);
        Assert.Equal(sedang.Id, hasil.Items[0].Id);
        Assert.DoesNotContain(hasil.Items, i => i.Id == murah.Id || i.Id == mahal.Id);
    }

    [Fact]
    public async Task Filter_satuan_harga()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Satuan-{Guid.NewGuid():N}";

        var harian = await api.CreateItemAsync(
            seller.Client, category: kategori, price: 100_000m, priceUnit: PriceUnits.Day);
        var bulanan = await api.CreateItemAsync(
            seller.Client, category: kategori, price: 100_000m, priceUnit: PriceUnits.Month);

        var hasil = await SearchAsync(api.CreateClient(),
            $"category={kategori}&priceUnit={PriceUnits.Month}");

        Assert.Single(hasil!.Items);
        Assert.Equal(bulanan.Id, hasil.Items[0].Id);
        Assert.DoesNotContain(hasil.Items, i => i.Id == harian.Id);
    }

    [Fact]
    public async Task Barang_yang_sudah_dibooking_hilang_dari_hasil_untuk_tanggal_itu()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RegisterAsync(Roles.Renter);
        var kategori = $"Uji-Booking-{Guid.NewGuid():N}";

        var terpakai = await api.CreateItemAsync(seller.Client, category: kategori);
        var bebas = await api.CreateItemAsync(seller.Client, category: kategori);

        var mulai = DateTimeOffset.UtcNow.AddDays(10);
        var selesai = mulai.AddDays(2);

        await api.SeedBookingAsync(terpakai.Id, renter.User.Id, mulai, selesai);

        var hasil = await SearchAsync(api.CreateClient(),
            $"category={kategori}" +
            $"&availableFrom={Uri.EscapeDataString(mulai.ToString("O"))}" +
            $"&availableTo={Uri.EscapeDataString(selesai.ToString("O"))}");

        Assert.Single(hasil!.Items);
        Assert.Equal(bebas.Id, hasil.Items[0].Id);
    }

    [Fact]
    public async Task Booking_yang_berakhir_persis_saat_rentang_dimulai_bukan_bentrokan()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RegisterAsync(Roles.Renter);
        var kategori = $"Uji-Sambung-{Guid.NewGuid():N}";

        var item = await api.CreateItemAsync(seller.Client, category: kategori);

        var batas = DateTimeOffset.UtcNow.AddDays(20);

        await api.SeedBookingAsync(item.Id, renter.User.Id, batas.AddDays(-2), batas);

        var hasil = await SearchAsync(api.CreateClient(),
            $"category={kategori}" +
            $"&availableFrom={Uri.EscapeDataString(batas.ToString("O"))}" +
            $"&availableTo={Uri.EscapeDataString(batas.AddDays(2).ToString("O"))}");

        Assert.Contains(hasil!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Booking_yang_dibatalkan_tidak_lagi_menahan_slot()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RegisterAsync(Roles.Renter);
        var kategori = $"Uji-Batal-{Guid.NewGuid():N}";

        var item = await api.CreateItemAsync(seller.Client, category: kategori);

        var mulai = DateTimeOffset.UtcNow.AddDays(30);
        var selesai = mulai.AddDays(2);

        await api.SeedBookingAsync(item.Id, renter.User.Id, mulai, selesai, status: "cancelled");

        var hasil = await SearchAsync(api.CreateClient(),
            $"category={kategori}" +
            $"&availableFrom={Uri.EscapeDataString(mulai.ToString("O"))}" +
            $"&availableTo={Uri.EscapeDataString(selesai.ToString("O"))}");

        Assert.Contains(hasil!.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Available_from_tanpa_available_to_ditolak()
    {
        var response = await api.CreateClient().GetAsync(
            $"/items?availableFrom={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Available_to_sebelum_available_from_ditolak()
    {
        var akhir = DateTimeOffset.UtcNow;
        var awal = akhir.AddDays(3);

        var response = await api.CreateClient().GetAsync(
            $"/items?availableFrom={Uri.EscapeDataString(awal.ToString("O"))}" +
            $"&availableTo={Uri.EscapeDataString(akhir.ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Urut_harga_menaik_dan_menurun()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Urut-{Guid.NewGuid():N}";

        await api.CreateItemAsync(seller.Client, category: kategori, price: 300_000m);
        await api.CreateItemAsync(seller.Client, category: kategori, price: 100_000m);
        await api.CreateItemAsync(seller.Client, category: kategori, price: 200_000m);

        var menaik = await SearchAsync(api.CreateClient(),
            $"category={kategori}&sort={ItemSortOptions.PriceAsc}");
        var menurun = await SearchAsync(api.CreateClient(),
            $"category={kategori}&sort={ItemSortOptions.PriceDesc}");

        Assert.Equal([100_000m, 200_000m, 300_000m], menaik!.Items.Select(i => i.Price));
        Assert.Equal([300_000m, 200_000m, 100_000m], menurun!.Items.Select(i => i.Price));
    }

    [Fact]
    public async Task Urutan_tidak_dikenal_ditolak()
    {
        var response = await api.CreateClient().GetAsync("/items?sort=termurah");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Halaman_membagi_hasil_tanpa_mengulang_atau_melewatkan()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Halaman-{Guid.NewGuid():N}";

        for (var i = 0; i < 5; i++)
        {
            await api.CreateItemAsync(seller.Client, category: kategori, price: 99_000m);
        }

        var satu = await SearchAsync(api.CreateClient(),
            $"category={kategori}&pageSize=2&page=1&sort={ItemSortOptions.PriceAsc}");
        var dua = await SearchAsync(api.CreateClient(),
            $"category={kategori}&pageSize=2&page=2&sort={ItemSortOptions.PriceAsc}");
        var tiga = await SearchAsync(api.CreateClient(),
            $"category={kategori}&pageSize=2&page=3&sort={ItemSortOptions.PriceAsc}");

        Assert.Equal(5, satu!.Total);
        Assert.Equal(3, satu.TotalPages);
        Assert.Equal(2, satu.Items.Count);
        Assert.Equal(2, dua!.Items.Count);
        Assert.Single(tiga!.Items);

        var semua = satu.Items.Concat(dua.Items).Concat(tiga.Items).Select(i => i.Id).ToList();
        Assert.Equal(5, semua.Distinct().Count());
    }

    [Fact]
    public async Task Ukuran_halaman_di_luar_batas_ditolak()
    {
        var response = await api.CreateClient().GetAsync("/items?pageSize=1000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Foto_utama_adalah_yang_sort_order_terkecil()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Foto-{Guid.NewGuid():N}";
        var item = await api.CreateItemAsync(seller.Client, category: kategori);

        var kedua = await ItemPhotoTests.UploadAsync(seller.Client, item.Id, ItemPhotoTests.Png(), sortOrder: 5);
        var pertama = await ItemPhotoTests.UploadAsync(seller.Client, item.Id, ItemPhotoTests.Png(), sortOrder: 1);
        await api.ApproveItemAsync(item.Id);

        var hasil = await SearchAsync(api.CreateClient(), $"category={kategori}");

        Assert.Equal(pertama.Url, hasil!.Items[0].PrimaryPhotoUrl);
        Assert.NotEqual(kedua.Url, hasil.Items[0].PrimaryPhotoUrl);
    }

    private static Task<List<ItemCategoryResponse>?> KategoriAsync(HttpClient client) =>
        client.GetFromJsonAsync<List<ItemCategoryResponse>>("/items/categories");

    [Fact]
    public async Task Kategori_diturunkan_dari_barang_yang_benar_benar_tampil()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Kat-{Guid.NewGuid():N}";

        await api.CreateItemAsync(seller.Client, category: kategori);
        await api.CreateItemAsync(seller.Client, category: kategori);

        var daftar = await KategoriAsync(api.CreateClient());

        Assert.Single(daftar!, k => k.Category == kategori);
        Assert.Equal(2, daftar!.Single(k => k.Category == kategori).Count);
    }

    [Fact]
    public async Task Kategori_membawa_foto_barang_di_dalamnya()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Kat-{Guid.NewGuid():N}";

        var barang = await api.CreateItemAsync(seller.Client, category: kategori);
        var foto = await ItemPhotoTests.UploadAsync(seller.Client, barang.Id, ItemPhotoTests.Png());
        await api.ApproveItemAsync(barang.Id);

        var daftar = await KategoriAsync(api.CreateClient());

        Assert.Equal(foto.Url, daftar!.Single(k => k.Category == kategori).PhotoUrl);
    }

    [Fact]
    public async Task Kategori_tanpa_satu_pun_foto_memulangkan_null()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Kat-{Guid.NewGuid():N}";

        await api.CreateItemAsync(seller.Client, category: kategori);

        var daftar = await KategoriAsync(api.CreateClient());

        Assert.Null(daftar!.Single(k => k.Category == kategori).PhotoUrl);
    }

    [Fact]
    public async Task Kategori_tidak_memuat_barang_pemilik_yang_belum_terverifikasi()
    {
        var seller = await api.SellerAsync();
        var kategori = $"Uji-Kat-{Guid.NewGuid():N}";

        await api.CreateItemAsync(seller.Client, category: kategori);

        var daftar = await KategoriAsync(api.CreateClient());

        Assert.DoesNotContain(daftar!, k => k.Category == kategori);
    }

    [Fact]
    public async Task Kategori_dikelompokkan_tanpa_peduli_huruf_besar_kecil()
    {
        var seller = await api.VerifiedSellerAsync();
        var kategori = $"Uji-Kat-{Guid.NewGuid():N}";

        await api.CreateItemAsync(seller.Client, category: kategori.ToUpperInvariant());
        await api.CreateItemAsync(seller.Client, category: kategori.ToLowerInvariant());

        var daftar = await KategoriAsync(api.CreateClient());

        var cocok = daftar!
            .Where(k => string.Equals(k.Category, kategori, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(cocok);
        Assert.Equal(2, cocok[0].Count);
    }

    [Fact]
    public async Task Barang_tanpa_riwayat_tidak_mengarang_penilaian()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client, title: $"Tanpa Riwayat {Guid.NewGuid():N}");

        var hasil = await SearchAsync(api.CreateClient(), $"q={Uri.EscapeDataString(item.Title)}");
        var kartu = Assert.Single(hasil!.Items, i => i.Id == item.Id);

        Assert.Null(kartu.RatingAverage);
        Assert.Equal(0, kartu.RatingCount);
        Assert.Equal(0, kartu.RentedCount);
    }

    [Fact]
    public async Task Kartu_membawa_rata_rata_penilaian_dan_jumlah_sewa_selesai()
    {
        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2610).AddHours(9);
        var skenario = await api.ActiveBookingAsync(mulai, mulai.AddDays(2));

        (await skenario.Seller.Client.PostAsync($"/bookings/{skenario.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();

        (await skenario.Renter.Client.PostAsJsonAsync(
            $"/bookings/{skenario.Booking.Id}/reviews",
            new CreateReviewRequest { Rating = 4, Comment = "Barang sesuai deskripsi." }))
            .EnsureSuccessStatusCode();

        var hasil = await SearchAsync(api.CreateClient(),
            $"q={Uri.EscapeDataString(skenario.Booking.ItemTitle)}");

        var kartu = Assert.Single(hasil!.Items, i => i.Id == skenario.Booking.ItemId);

        Assert.Equal(4d, kartu.RatingAverage);
        Assert.Equal(1, kartu.RatingCount);
        Assert.Equal(1, kartu.RentedCount);
    }

    [Fact]
    public async Task Sewa_membawa_foto_barang_di_setiap_jalur()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        await ItemPhotoTests.UploadAsync(seller.Client, item.Id, ItemPhotoTests.Png());
        await api.ApproveItemAsync(item.Id);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2620).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(1));

        Assert.NotNull(booking.ItemPhotoUrl);
        Assert.StartsWith("/uploads/", booking.ItemPhotoUrl);

        var dibaca = await renter.Client.GetFromJsonAsync<BookingResponse>($"/bookings/{booking.Id}");

        Assert.Equal(booking.ItemPhotoUrl, dibaca!.ItemPhotoUrl);

        var daftar = await renter.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>("/bookings");

        Assert.Equal(booking.ItemPhotoUrl,
            Assert.Single(daftar!.Items, b => b.Id == booking.Id).ItemPhotoUrl);
    }
}
