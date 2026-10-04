using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class StaffLoginTests(ApiFactory api)
{
    private Task<HttpResponseMessage> MasukPublikAsync(string email, string password) =>
        api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = email, Password = password });

    private Task<HttpResponseMessage> MasukStafAsync(string email, string password) =>
        api.CreateClient().PostAsJsonAsync("/auth/staff/login",
            new LoginRequest { Email = email, Password = password });

    private async Task<(string Email, Guid Id)> AdminBaruAsync()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = new CreateAdminRequest
        {
            Name     = "Admin Pintu",
            Email    = ApiFactory.UniqueEmail("admin-pintu"),
            Password = ApiFactory.Password
        };

        var dibuat = await (await owner.PostAsJsonAsync("/owner/admins", permintaan))
            .Content.ReadFromJsonAsync<AdminAccountResponse>();

        return (permintaan.Email, dibuat!.Id);
    }

    [Fact]
    public async Task Owner_masuk_lewat_pintu_staf_dan_tokennya_membuka_panel_owner()
    {
        var masuk = await api.StaffLoginAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.Equal(Roles.Owner, masuk.User.Role);

        var setelan = await api.ClientWithToken(masuk.AccessToken).GetAsync("/owner/settings");
        Assert.Equal(HttpStatusCode.OK, setelan.StatusCode);
    }

    [Fact]
    public async Task Admin_masuk_lewat_pintu_staf_dan_langsung_bisa_bekerja()
    {
        var (email, _) = await AdminBaruAsync();

        var masuk = await api.StaffLoginAsync(email, ApiFactory.Password);

        Assert.Equal(Roles.Admin, masuk.User.Role);

        var antrean = await api.ClientWithToken(masuk.AccessToken).GetAsync("/admin/sellers/pending");
        Assert.Equal(HttpStatusCode.OK, antrean.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Non_staf_ditolak_di_pintu_staf(string role)
    {
        var akun = await api.RegisterAsync(role);

        var masuk = await MasukStafAsync(akun.User.Email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }

    [Fact]
    public async Task Kata_sandi_salah_di_pintu_staf_ditolak()
    {
        var (email, _) = await AdminBaruAsync();

        var masuk = await MasukStafAsync(email, "SalahTotal#2026");

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }

    [Fact]
    public async Task Penolakan_non_staf_tidak_bisa_dibedakan_dari_email_tak_terdaftar()
    {
        var akun = await api.RegisterAsync(Roles.Renter);

        var bukanStaf = await MasukStafAsync(akun.User.Email, ApiFactory.Password);
        var emailHantu = await MasukStafAsync(ApiFactory.UniqueEmail("hantu"), ApiFactory.Password);

        Assert.Equal(bukanStaf.StatusCode, emailHantu.StatusCode);
        Assert.Equal(
            await ApiFactory.BodyWithoutTraceIdAsync(bukanStaf),
            await ApiFactory.BodyWithoutTraceIdAsync(emailHantu));
    }

    [Fact]
    public async Task Staf_yang_dicabut_ditolak_403_di_pintu_staf()
    {
        var owner = await api.ClientAsOwnerAsync();
        var (email, id) = await AdminBaruAsync();

        await owner.PostAsJsonAsync($"/owner/admins/{id}/access",
            new SetAdminAccessRequest { Active = false });

        var masuk = await MasukStafAsync(email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Forbidden, masuk.StatusCode);
    }

    [Fact]
    public async Task Sesi_dari_pintu_staf_diperpanjang_lewat_endpoint_yang_sama()
    {
        var masuk = await api.StaffLoginAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        var perpanjang = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = masuk.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, perpanjang.StatusCode);

        var baru = await perpanjang.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(masuk.User.Id, baru!.User.Id);
        Assert.Equal(Roles.Owner, baru.User.Role);
    }

    [Fact]
    public async Task Superadmin_ditolak_di_pintu_publik()
    {
        var masuk = await MasukPublikAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }

    [Fact]
    public async Task Admin_ditolak_di_pintu_publik()
    {
        var (email, _) = await AdminBaruAsync();

        var masuk = await MasukPublikAsync(email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Pengguna_biasa_tetap_dilayani_pintu_publik(string role)
    {
        var akun = await api.RegisterAsync(role);

        var masuk = await MasukPublikAsync(akun.User.Email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.OK, masuk.StatusCode);
    }

    [Fact]
    public async Task Penolakan_staf_di_pintu_publik_identik_dengan_email_tak_terdaftar()
    {
        var staf = await MasukPublikAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var emailHantu = await MasukPublikAsync(ApiFactory.UniqueEmail("hantu"), ApiFactory.Password);

        Assert.Equal(staf.StatusCode, emailHantu.StatusCode);
        Assert.Equal(
            await ApiFactory.BodyWithoutTraceIdAsync(staf),
            await ApiFactory.BodyWithoutTraceIdAsync(emailHantu));
    }

    [Fact]
    public async Task Staf_yang_dicabut_dijawab_401_di_pintu_publik_bukan_403()
    {
        var owner = await api.ClientAsOwnerAsync();
        var (email, id) = await AdminBaruAsync();

        await owner.PostAsJsonAsync($"/owner/admins/{id}/access",
            new SetAdminAccessRequest { Active = false });

        var masuk = await MasukPublikAsync(email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }
}
