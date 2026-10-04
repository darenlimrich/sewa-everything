using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory api)
{

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Pendaftaran_role_publik_berhasil(string role)
    {
        var auth = await api.RegisterAsync(role);

        Assert.Equal(role, auth.User.Role);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Admin)]
    [InlineData("OWNER")]
    [InlineData("superadmin")]
    [InlineData("")]
    public async Task Pendaftaran_role_istimewa_ditolak(string role)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name     = "Penyusup",
            Email    = ApiFactory.UniqueEmail("nakal"),
            Password = ApiFactory.Password,
            Role     = role
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Seller_baru_belum_terverifikasi()
    {
        var auth = await api.RegisterAsync(Roles.Seller);

        Assert.False(auth.User.IsVerified);
    }

    [Fact]
    public async Task Email_ganda_ditolak()
    {
        var email = ApiFactory.UniqueEmail("kembar");
        await api.RegisterAsync(Roles.Renter, email);

        var kedua = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name = "Kembar", Email = email, Password = ApiFactory.Password, Role = Roles.Renter
        });

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Fact]
    public async Task Email_ganda_beda_kapital_ditolak()
    {
        var email = ApiFactory.UniqueEmail("kapital");
        await api.RegisterAsync(Roles.Renter, email);

        var kedua = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name = "Kembar", Email = email.ToUpperInvariant(),
            Password = ApiFactory.Password, Role = Roles.Renter
        });

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Theory]
    [InlineData("pendek")]
    [InlineData("")]
    public async Task Kata_sandi_lemah_ditolak(string password)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name = "Uji", Email = ApiFactory.UniqueEmail("lemah"),
            Password = password, Role = Roles.Renter
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Email_tidak_valid_ditolak()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name = "Uji", Email = "bukan-email", Password = ApiFactory.Password, Role = Roles.Renter
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Masuk_dengan_kredensial_benar_berhasil()
    {
        var email = ApiFactory.UniqueEmail("masuk");
        await api.RegisterAsync(Roles.Renter, email);

        var auth = await api.LoginAsync(email, ApiFactory.Password);

        Assert.Equal(email, auth.User.Email);
    }

    [Fact]
    public async Task Masuk_dengan_email_kapital_berhasil()
    {
        var email = ApiFactory.UniqueEmail("kapital-masuk");
        await api.RegisterAsync(Roles.Renter, email);

        var auth = await api.LoginAsync(email.ToUpperInvariant(), ApiFactory.Password);

        Assert.Equal(email, auth.User.Email);
    }

    [Fact]
    public async Task Masuk_dengan_kata_sandi_salah_ditolak()
    {
        var email = ApiFactory.UniqueEmail("salah");
        await api.RegisterAsync(Roles.Renter, email);

        var response = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = email, Password = "KataSandiSalah#1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Masuk_dengan_email_tak_terdaftar_ditolak()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = ApiFactory.UniqueEmail("hantu"), Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pesan_gagal_masuk_tidak_membocorkan_email_terdaftar()
    {
        var email = ApiFactory.UniqueEmail("bocor");
        await api.RegisterAsync(Roles.Renter, email);

        var sandiSalah = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = email, Password = "KataSandiSalah#1" });
        var emailHantu = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = ApiFactory.UniqueEmail("hantu"), Password = ApiFactory.Password });

        Assert.Equal(sandiSalah.StatusCode, emailHantu.StatusCode);
        Assert.Equal(
            await ApiFactory.BodyWithoutTraceIdAsync(sandiSalah),
            await ApiFactory.BodyWithoutTraceIdAsync(emailHantu));
    }

    [Fact]
    public async Task Me_mengembalikan_profil_pemegang_token()
    {
        var auth = await api.RegisterAsync(Roles.Seller);
        var client = api.ClientWithToken(auth.AccessToken);

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");

        Assert.Equal(auth.User.Id, me!.Id);
        Assert.Equal(Roles.Seller, me.Role);
    }

    [Fact]
    public async Task Respons_tidak_pernah_memuat_hash_kata_sandi()
    {
        var email = ApiFactory.UniqueEmail("hash");

        var daftar = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name = "Uji", Email = email, Password = ApiFactory.Password, Role = Roles.Renter
        });
        var isiDaftar = await daftar.Content.ReadAsStringAsync();

        var auth = await api.LoginAsync(email, ApiFactory.Password);
        var isiMe = await api.ClientWithToken(auth.AccessToken).GetStringAsync("/auth/me");

        foreach (var isi in new[] { isiDaftar, isiMe })
        {
            Assert.DoesNotContain("passwordHash", isi, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password_hash", isi, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ApiFactory.Password, isi, StringComparison.Ordinal);
        }
    }
}
