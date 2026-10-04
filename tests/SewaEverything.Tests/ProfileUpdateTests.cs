using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ProfileUpdateTests(ApiFactory api)
{
    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Pemilik_akun_dapat_mengubah_nama_dan_telepon(string role)
    {
        var client = await api.ClientAsAsync(role);

        var response = await client.PutAsJsonAsync("/auth/me", new UpdateProfileRequest
        {
            Name  = "Nama Baru",
            Phone = "081200001111"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Nama Baru", updated!.Name);
        Assert.Equal("081200001111", updated.Phone);

        var reread = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Equal("Nama Baru", reread!.Name);
        Assert.Equal("081200001111", reread.Phone);
    }

    [Fact]
    public async Task Telepon_dapat_dikosongkan()
    {
        var auth = await api.RegisterAsync(Roles.Renter, phone: "081200002222");
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PutAsJsonAsync("/auth/me", new UpdateProfileRequest
        {
            Name  = auth.User.Name,
            Phone = null
        });

        response.EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Null(reread!.Phone);
    }

    [Fact]
    public async Task Nama_dipangkas_spasinya()
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var response = await client.PutAsJsonAsync("/auth/me", new UpdateProfileRequest
        {
            Name = "   Sari Dewi   "
        });

        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Sari Dewi", updated!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData(" A ")]
    public async Task Nama_kosong_atau_terlalu_pendek_ditolak(string name)
    {
        var client = await api.ClientAsAsync(Roles.Renter);

        var response = await client.PutAsJsonAsync("/auth/me", new UpdateProfileRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nama_yang_ditolak_tidak_mengubah_apa_pun()
    {
        var auth = await api.RegisterAsync(Roles.Renter, phone: "081200003333");
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PutAsJsonAsync("/auth/me", new UpdateProfileRequest
        {
            Name  = "",
            Phone = "081299998888"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var reread = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Equal(auth.User.Name, reread!.Name);
        Assert.Equal("081200003333", reread.Phone);
    }

    [Fact]
    public async Task Email_dan_peran_tidak_dapat_diubah_lewat_endpoint_ini()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PutAsJsonAsync("/auth/me", new
        {
            name  = "Nama Lain",
            email = "penyerang@uji.local",
            role  = Roles.Owner
        });

        response.EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Equal(auth.User.Email, reread!.Email);
        Assert.Equal(Roles.Renter, reread.Role);
    }

    [Fact]
    public async Task Verifikasi_pemilik_barang_tidak_ikut_berubah()
    {
        var seller = await api.VerifiedSellerAsync();
        var client = seller.Client;

        var response = await client.PutAsJsonAsync("/auth/me", new
        {
            name       = "Toko Ganti Nama",
            isVerified = false
        });

        response.EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.True(reread!.IsVerified);
    }

    [Fact]
    public async Task Tanpa_token_ditolak()
    {
        var response = await api.CreateClient().PutAsJsonAsync("/auth/me",
            new UpdateProfileRequest { Name = "Anonim" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Satu_akun_tidak_dapat_mengubah_akun_lain()
    {
        var korban = await api.RegisterAsync(Roles.Renter);
        var penyerang = await api.RegisterAsync(Roles.Renter);

        var client = api.ClientWithToken(penyerang.AccessToken);

        var response = await client.PutAsJsonAsync("/auth/me", new
        {
            id   = korban.User.Id,
            name = "Diambil Alih"
        });

        response.EnsureSuccessStatusCode();

        var korbanClient = api.ClientWithToken(korban.AccessToken);
        var korbanFresh = await korbanClient.GetFromJsonAsync<UserResponse>("/auth/me");

        Assert.Equal(korban.User.Name, korbanFresh!.Name);
    }
}
