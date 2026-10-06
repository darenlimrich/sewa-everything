using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using SewaEverything.Infrastructure.Storage;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class DatabasePhotoStorageTests(ApiFactory api) : IDisposable
{
    private readonly WebApplicationFactory<Program> _db =
        api.WithWebHostBuilder(b => b.UseSetting("Storage:Photos:Provider", PhotoStorageOptions.DatabaseProvider));

    public void Dispose() => _db.Dispose();

    private HttpClient Pakai(HttpClient asal)
    {
        var client = _db.CreateClient();
        client.DefaultRequestHeaders.Authorization = asal.DefaultRequestHeaders.Authorization;
        return client;
    }

    [Fact]
    public async Task Foto_tersimpan_di_database_dan_dilayani_di_path_yang_sama()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var foto = await ItemPhotoTests.UploadAsync(Pakai(seller.Client), item.Id, ItemPhotoTests.Png());

        Assert.StartsWith("/uploads/", foto.Url);
        Assert.False(File.Exists(Path.Combine(api.PhotoRoot, foto.Url.Split('/')[^1])));

        var berkas = await _db.CreateClient().GetAsync(foto.Url);

        Assert.Equal(HttpStatusCode.OK, berkas.StatusCode);
        Assert.Equal("image/png", berkas.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", berkas.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("immutable", berkas.Headers.CacheControl?.ToString());
        Assert.Equal(ItemPhotoTests.Png(), await berkas.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Hapus_foto_menghapus_isinya_dari_database()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);
        var client = Pakai(seller.Client);
        var foto = await ItemPhotoTests.UploadAsync(client, item.Id, ItemPhotoTests.Jpeg(), contentType: "image/jpeg", fileName: "a.jpg");

        var hapus = await client.DeleteAsync($"/items/{item.Id}/photos/{foto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);

        var berkas = await _db.CreateClient().GetAsync(foto.Url);
        Assert.Equal(HttpStatusCode.NotFound, berkas.StatusCode);
    }

    [Theory]
    [InlineData("/uploads/tidak-ada.png")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.png")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.svg")]
    public async Task Nama_yang_tidak_ada_atau_tidak_sah_404(string url)
    {
        var berkas = await _db.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, berkas.StatusCode);
    }
}
