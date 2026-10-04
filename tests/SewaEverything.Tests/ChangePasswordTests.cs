using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class ChangePasswordTests(ApiFactory api)
{
    private const string SandiBaru = "Melati#Senja#77";

    [Fact]
    public async Task Pemilik_akun_dapat_mengganti_sandinya_sendiri()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = ApiFactory.Password,
            NewPassword     = SandiBaru
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var masuk = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = auth.User.Email, Password = SandiBaru });

        Assert.Equal(HttpStatusCode.OK, masuk.StatusCode);
    }

    [Fact]
    public async Task Sandi_lama_tidak_berlaku_lagi()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        (await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = ApiFactory.Password,
            NewPassword     = SandiBaru
        })).EnsureSuccessStatusCode();

        var masuk = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = auth.User.Email, Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }

    [Fact]
    public async Task Sandi_saat_ini_yang_salah_ditolak_dan_tidak_mengubah_apa_pun()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "Bukan#Sandi#Nya99",
            NewPassword     = SandiBaru
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var masuk = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = auth.User.Email, Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, masuk.StatusCode);
    }

    [Theory]
    [InlineData("pendek")]
    [InlineData("password1234")]
    [InlineData("081234567890")]
    public async Task Sandi_baru_yang_lemah_ditolak(string lemah)
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = ApiFactory.Password,
            NewPassword     = lemah
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sandi_baru_yang_sama_dengan_yang_lama_ditolak()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = ApiFactory.Password,
            NewPassword     = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sandi_baru_yang_memuat_nama_pemiliknya_ditolak()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = ApiFactory.Password,
            NewPassword     = $"{auth.User.Name}#Rahasia26"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ganti_sandi_mencabut_seluruh_sesi_lama()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var client = api.ClientWithToken(auth.AccessToken);

        (await client.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = ApiFactory.Password,
            NewPassword     = SandiBaru
        })).EnsureSuccessStatusCode();

        var perpanjang = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = auth.RefreshToken });

        Assert.NotEqual(HttpStatusCode.OK, perpanjang.StatusCode);
    }

    [Fact]
    public async Task Tanpa_token_ditolak()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/change-password",
            new ChangePasswordRequest
            {
                CurrentPassword = ApiFactory.Password,
                NewPassword     = SandiBaru
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
