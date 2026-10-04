using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class AvatarTests(ApiFactory api)
{
    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, byte[] bytes,
        string fileName = "avatar.png", string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        return await client.PostAsync("/auth/me/photo", form);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Pemilik_akun_dapat_mengunggah_foto_profilnya(string role)
    {
        var client = await api.ClientAsAsync(role);

        Assert.Null((await client.GetFromJsonAsync<UserResponse>("/auth/me"))!.AvatarUrl);

        var response = await PostAsync(client, ItemPhotoTests.Png());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.False(string.IsNullOrWhiteSpace(updated!.AvatarUrl));

        var reread = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Equal(updated.AvatarUrl, reread!.AvatarUrl);
    }

    [Fact]
    public async Task Foto_yang_diunggah_benar_benar_dapat_dibuka()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var response = await PostAsync(client, ItemPhotoTests.Jpeg(), "avatar.jpg", "image/jpeg");
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();

        var berkas = await client.GetAsync(user!.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, berkas.StatusCode);
        Assert.Equal("image/jpeg", berkas.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unggahan_kedua_menggantikan_yang_pertama_dan_berkas_lamanya_dibuang()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var pertama = await PostAsync(client, ItemPhotoTests.Png());
        pertama.EnsureSuccessStatusCode();
        var lama = (await pertama.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl!;

        var kedua = await PostAsync(client, ItemPhotoTests.Jpeg(), "avatar.jpg", "image/jpeg");
        kedua.EnsureSuccessStatusCode();
        var baru = (await kedua.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl!;

        Assert.NotEqual(lama, baru);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(baru)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(lama)).StatusCode);
    }

    [Fact]
    public async Task Foto_dapat_dihapus_dan_berkasnya_ikut_hilang()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var unggah = await PostAsync(client, ItemPhotoTests.Png());
        unggah.EnsureSuccessStatusCode();
        var url = (await unggah.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl!;

        var hapus = await client.DeleteAsync("/auth/me/photo");
        Assert.Equal(HttpStatusCode.OK, hapus.StatusCode);
        Assert.Null((await hapus.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Menghapus_saat_belum_ada_foto_tidak_menggagalkan_apa_pun()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var hapus = await client.DeleteAsync("/auth/me/photo");

        Assert.Equal(HttpStatusCode.OK, hapus.StatusCode);
        Assert.Null((await hapus.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl);
    }

    [Fact]
    public async Task Berkas_yang_bukan_gambar_ditolak()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var response = await PostAsync(client, "<?php echo 1; ?>"u8.ToArray(), "avatar.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await client.GetFromJsonAsync<UserResponse>("/auth/me"))!.AvatarUrl);
    }

    [Fact]
    public async Task Berkas_yang_hanya_berawalan_penanda_png_ikut_ditolak()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var palsu = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat("<?php echo 1; ?>"u8.ToArray()).ToArray();

        var response = await PostAsync(client, palsu);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Berkas_kosong_ditolak()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var response = await PostAsync(client, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tanpa_token_tidak_dapat_menyentuh_foto_profil()
    {
        var anon = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anon, ItemPhotoTests.Png())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.DeleteAsync("/auth/me/photo")).StatusCode);
    }

    [Fact]
    public async Task Foto_satu_akun_tidak_menyentuh_akun_lain()
    {
        var a = await api.ClientAsAsync(Roles.Renter);
        var b = await api.ClientAsAsync(Roles.Renter);

        var unggah = await PostAsync(a, ItemPhotoTests.Png());
        unggah.EnsureSuccessStatusCode();

        Assert.NotNull((await a.GetFromJsonAsync<UserResponse>("/auth/me"))!.AvatarUrl);
        Assert.Null((await b.GetFromJsonAsync<UserResponse>("/auth/me"))!.AvatarUrl);
    }

    [Fact]
    public async Task Staf_juga_punya_foto_profil()
    {
        var client = await api.ClientAsAdminAsync();

        var response = await PostAsync(client, ItemPhotoTests.Png());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl);
    }
}
