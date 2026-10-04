using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ItemPhotoTests(ApiFactory api)
{

    public static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static byte[] Jpeg() => Convert.FromBase64String(JpegBase64);

    private const string JpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAA0JCgsKCA0LCwsPDg0QFCEVFBISFCgdHhghMCoyMS8qLi00O0tANDhH" +
        "OS0uQllCR05QVFVUMz9dY1xSYktTVFH/2wBDAQ4PDxQRFCcVFSdRNi42UVFRUVFRUVFRUVFRUVFRUVFRUVFRUVFR" +
        "UVFRUVFRUVFRUVFRUVFRUVFRUVFRUVFRUVH/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAA" +
        "AAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAk" +
        "M2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKT" +
        "lJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QA" +
        "HwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdh" +
        "cRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hp" +
        "anN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk" +
        "5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDnqKKK5j7M/9k=";

    public static async Task<ItemPhotoResponse> UploadAsync(
        HttpClient client, Guid itemId, byte[] bytes, int? sortOrder = null,
        string fileName = "foto.png", string contentType = "image/png")
    {
        var response = await PostAsync(client, itemId, bytes, sortOrder, fileName, contentType);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ItemPhotoResponse>())!;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, Guid itemId, byte[] bytes, int? sortOrder = null,
        string fileName = "foto.png", string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        if (sortOrder is { } order)
        {
            form.Add(new StringContent(order.ToString()), "sortOrder");
        }

        return await client.PostAsync($"/items/{itemId}/photos", form);
    }

    [Fact]
    public async Task Seller_bisa_mengunggah_foto_dan_fotonya_bisa_dibuka()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var foto = await UploadAsync(seller.Client, item.Id, Png());

        Assert.StartsWith("/uploads/", foto.Url);

        var berkas = await api.CreateClient().GetAsync(foto.Url);

        Assert.Equal(HttpStatusCode.OK, berkas.StatusCode);
        Assert.Equal("image/png", berkas.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png(), await berkas.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Ekstensi_berkas_ditentukan_dari_isinya_bukan_dari_nama()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var foto = await UploadAsync(
            seller.Client, item.Id, Png(), fileName: "sebenarnya-png.jpg", contentType: "image/jpeg");

        Assert.EndsWith(".png", foto.Url);
    }

    [Fact]
    public async Task Format_jpeg_diterima()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var foto = await UploadAsync(
            seller.Client, item.Id, Jpeg(), fileName: "a.jpg", contentType: "image/jpeg");

        Assert.EndsWith(".jpg", foto.Url);
    }

    [Fact]
    public async Task Berkas_bukan_gambar_ditolak_walau_mengaku_jpeg()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var payload = "<?php system($_GET['c']); ?>"u8.ToArray();

        var response = await PostAsync(
            seller.Client, item.Id, payload, fileName: "foto.jpg", contentType: "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Berkas_kosong_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var response = await PostAsync(seller.Client, item.Id, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Berkas_melebihi_batas_ukuran_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var kebesaran = new byte[ApiFactory.MaxPhotoBytes + 1];
        Png().CopyTo(kebesaran, 0);

        var response = await PostAsync(seller.Client, item.Id, kebesaran);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Foto_melebihi_batas_jumlah_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        for (var i = 0; i < ApiFactory.MaxPhotosPerItem; i++)
        {
            await UploadAsync(seller.Client, item.Id, Png());
        }

        var response = await PostAsync(seller.Client, item.Id, Png());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Seller_lain_tidak_bisa_mengunggah_ke_barang_orang()
    {
        var pemilik = await api.VerifiedSellerAsync();
        var penyusup = await api.VerifiedSellerAsync();

        var item = await api.CreateItemAsync(pemilik.Client);

        var response = await PostAsync(penyusup.Client, item.Id, Png());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unggah_ke_barang_tidak_ada_404()
    {
        var seller = await api.VerifiedSellerAsync();

        var response = await PostAsync(seller.Client, Guid.NewGuid(), Png());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Foto_tanpa_sort_order_ditambahkan_di_urutan_terakhir()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var pertama = await UploadAsync(seller.Client, item.Id, Png());
        var kedua = await UploadAsync(seller.Client, item.Id, Png());
        var ketiga = await UploadAsync(seller.Client, item.Id, Png());

        Assert.Equal(0, pertama.SortOrder);
        Assert.Equal(1, kedua.SortOrder);
        Assert.Equal(2, ketiga.SortOrder);
    }

    [Fact]
    public async Task Detail_barang_menampilkan_foto_berurutan()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var belakang = await UploadAsync(seller.Client, item.Id, Png(), sortOrder: 9);
        var depan = await UploadAsync(seller.Client, item.Id, Png(), sortOrder: 2);
        await api.ApproveItemAsync(item.Id);

        var detail = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");

        Assert.Equal([depan.Id, belakang.Id], detail!.Photos.Select(p => p.Id));
    }

    [Fact]
    public async Task Hapus_foto_menghilangkan_baris_dan_berkasnya()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);
        var foto = await UploadAsync(seller.Client, item.Id, Png());
        await api.ApproveItemAsync(item.Id);

        var hapus = await seller.Client.DeleteAsync($"/items/{item.Id}/photos/{foto.Id}");

        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);

        var detail = await api.CreateClient().GetFromJsonAsync<ItemDetailResponse>($"/items/{item.Id}");
        Assert.Empty(detail!.Photos);

        var berkas = await api.CreateClient().GetAsync(foto.Url);
        Assert.Equal(HttpStatusCode.NotFound, berkas.StatusCode);
    }

    [Fact]
    public async Task Seller_lain_tidak_bisa_menghapus_foto_orang()
    {
        var pemilik = await api.VerifiedSellerAsync();
        var penyusup = await api.VerifiedSellerAsync();

        var item = await api.CreateItemAsync(pemilik.Client);
        var foto = await UploadAsync(pemilik.Client, item.Id, Png());

        var response = await penyusup.Client.DeleteAsync($"/items/{item.Id}/photos/{foto.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var berkas = await api.CreateClient().GetAsync(foto.Url);
        Assert.Equal(HttpStatusCode.OK, berkas.StatusCode);
    }

    [Fact]
    public async Task Hapus_foto_milik_barang_lain_404()
    {
        var seller = await api.VerifiedSellerAsync();

        var satu = await api.CreateItemAsync(seller.Client);
        var dua = await api.CreateItemAsync(seller.Client);

        var foto = await UploadAsync(seller.Client, satu.Id, Png());

        var response = await seller.Client.DeleteAsync($"/items/{dua.Id}/photos/{foto.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Berkas_foto_dilayani_dengan_nosniff()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);
        var foto = await UploadAsync(seller.Client, item.Id, Png());

        var berkas = await api.CreateClient().GetAsync(foto.Url);

        Assert.Equal("nosniff", berkas.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Renter_tidak_bisa_mengunggah_foto()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var renter = await api.ClientAsAsync(Roles.Renter);

        var response = await PostAsync(renter, item.Id, Png());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Exif_gps_tidak_pernah_sampai_ke_berkas_yang_dilayani()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var exif = System.Text.Encoding.ASCII.GetBytes(
            "Exif  MM *GPSLatitude=-6.9034 GPSLongitude=107.6181");

        var asli = Jpeg();
        var panjang = exif.Length + 2;

        byte[] berexif =
        [
            asli[0], asli[1],
            0xFF, 0xE1, (byte)(panjang >> 8), (byte)(panjang & 0xFF),
            .. exif,
            .. asli[2..]
        ];

        var foto = await UploadAsync(seller.Client, item.Id, berexif, fileName: "kamera.jpg",
            contentType: "image/jpeg");

        var dilayani = await api.CreateClient().GetByteArrayAsync(foto.Url);

        Assert.DoesNotContain("GPSLatitude", System.Text.Encoding.ASCII.GetString(dilayani),
            StringComparison.Ordinal);
        Assert.Equal([0xFF, 0xD8], dilayani[..2]);
        Assert.Equal([0xFF, 0xD9], dilayani[^2..]);
    }
}
