using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class OwnerAdminTests(ApiFactory api)
{
    private static CreateAdminRequest Baru(string? email = null) => new()
    {
        Name     = "Admin Baru",
        Email    = email ?? ApiFactory.UniqueEmail("admin-owner"),
        Password = ApiFactory.Password,
        Phone    = "081234567890"
    };

    [Fact]
    public async Task Owner_mengangkat_admin_dan_admin_itu_langsung_bisa_bekerja()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        var response = await owner.PostAsJsonAsync("/owner/admins", permintaan);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var admin = await response.Content.ReadFromJsonAsync<AdminAccountResponse>();
        Assert.True(admin!.IsActive);
        Assert.Null(admin.DeactivatedAt);

        var masuk = await api.StaffLoginAsync(permintaan.Email, ApiFactory.Password);
        Assert.Equal(Roles.Admin, masuk.User.Role);

        var antrean = await api.ClientWithToken(masuk.AccessToken).GetAsync("/admin/sellers/pending");
        Assert.Equal(HttpStatusCode.OK, antrean.StatusCode);
    }

    [Fact]
    public async Task Admin_baru_muncul_di_daftar_owner()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        await owner.PostAsJsonAsync("/owner/admins", permintaan);

        var daftar = await owner.GetFromJsonAsync<List<AdminAccountResponse>>("/owner/admins");

        Assert.Contains(daftar!, a => a.Email == permintaan.Email && a.IsActive);
    }

    [Fact]
    public async Task Role_selundupan_di_body_diabaikan_admin_tetap_lahir_sebagai_admin()
    {
        var owner = await api.ClientAsOwnerAsync();
        var email = ApiFactory.UniqueEmail("penyusup");

        var response = await owner.PostAsJsonAsync("/owner/admins", new
        {
            name     = "Calon Owner",
            email,
            password = ApiFactory.Password,
            role     = Roles.Owner
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var masuk = await api.StaffLoginAsync(email, ApiFactory.Password);
        Assert.Equal(Roles.Admin, masuk.User.Role);

        var setelan = await api.ClientWithToken(masuk.AccessToken).GetAsync("/owner/settings");
        Assert.Equal(HttpStatusCode.Forbidden, setelan.StatusCode);
    }

    [Fact]
    public async Task Admin_tidak_bisa_mengangkat_admin_lain()
    {
        var admin = await api.ClientAsAdminAsync();

        var buat = await admin.PostAsJsonAsync("/owner/admins", Baru());
        var daftar = await admin.GetAsync("/owner/admins");

        Assert.Equal(HttpStatusCode.Forbidden, buat.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, daftar.StatusCode);
    }

    [Fact]
    public async Task Renter_tidak_bisa_membuka_daftar_admin()
    {
        var renter = await api.ClientAsAsync(Roles.Renter);

        Assert.Equal(HttpStatusCode.Forbidden, (await renter.GetAsync("/owner/admins")).StatusCode);
    }

    [Fact]
    public async Task Email_yang_sudah_dipakai_ditolak()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        await owner.PostAsJsonAsync("/owner/admins", permintaan);
        var kedua = await owner.PostAsJsonAsync("/owner/admins", permintaan);

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Fact]
    public async Task Kata_sandi_terlalu_pendek_ditolak()
    {
        var owner = await api.ClientAsOwnerAsync();

        var response = await owner.PostAsJsonAsync("/owner/admins", new CreateAdminRequest
        {
            Name     = "Admin Baru",
            Email    = ApiFactory.UniqueEmail("admin-pendek"),
            Password = "pendek"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_admin_yang_dicabut_langsung_mati_tanpa_menunggu_kedaluwarsa()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        var dibuat = await (await owner.PostAsJsonAsync("/owner/admins", permintaan))
            .Content.ReadFromJsonAsync<AdminAccountResponse>();

        var masuk = await api.StaffLoginAsync(permintaan.Email, ApiFactory.Password);
        var adminClient = api.ClientWithToken(masuk.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/admin/sellers/pending")).StatusCode);

        var cabut = await owner.PostAsJsonAsync($"/owner/admins/{dibuat!.Id}/access",
            new SetAdminAccessRequest { Active = false });

        Assert.Equal(HttpStatusCode.OK, cabut.StatusCode);

        var sesudah = await cabut.Content.ReadFromJsonAsync<AdminAccountResponse>();
        Assert.False(sesudah!.IsActive);
        Assert.NotNull(sesudah.DeactivatedAt);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await adminClient.GetAsync("/admin/sellers/pending")).StatusCode);
    }

    [Fact]
    public async Task Admin_yang_dicabut_tidak_bisa_masuk_lagi()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        var dibuat = await (await owner.PostAsJsonAsync("/owner/admins", permintaan))
            .Content.ReadFromJsonAsync<AdminAccountResponse>();

        await owner.PostAsJsonAsync($"/owner/admins/{dibuat!.Id}/access",
            new SetAdminAccessRequest { Active = false });

        var masuk = await api.CreateClient().PostAsJsonAsync("/auth/staff/login",
            new LoginRequest { Email = permintaan.Email, Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.Forbidden, masuk.StatusCode);
    }

    [Fact]
    public async Task Kata_sandi_salah_pada_akun_dicabut_tetap_dijawab_401()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        var dibuat = await (await owner.PostAsJsonAsync("/owner/admins", permintaan))
            .Content.ReadFromJsonAsync<AdminAccountResponse>();

        await owner.PostAsJsonAsync($"/owner/admins/{dibuat!.Id}/access",
            new SetAdminAccessRequest { Active = false });

        var masuk = await api.CreateClient().PostAsJsonAsync("/auth/staff/login",
            new LoginRequest { Email = permintaan.Email, Password = "SalahTotal#2026" });

        Assert.Equal(HttpStatusCode.Unauthorized, masuk.StatusCode);
    }

    [Fact]
    public async Task Akses_yang_dicabut_bisa_dipulihkan_lagi()
    {
        var owner = await api.ClientAsOwnerAsync();
        var permintaan = Baru();

        var dibuat = await (await owner.PostAsJsonAsync("/owner/admins", permintaan))
            .Content.ReadFromJsonAsync<AdminAccountResponse>();

        var masuk = await api.StaffLoginAsync(permintaan.Email, ApiFactory.Password);
        var adminClient = api.ClientWithToken(masuk.AccessToken);

        await owner.PostAsJsonAsync($"/owner/admins/{dibuat!.Id}/access",
            new SetAdminAccessRequest { Active = false });

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await adminClient.GetAsync("/admin/sellers/pending")).StatusCode);

        var pulih = await owner.PostAsJsonAsync($"/owner/admins/{dibuat.Id}/access",
            new SetAdminAccessRequest { Active = true });

        var hasil = await pulih.Content.ReadFromJsonAsync<AdminAccountResponse>();
        Assert.True(hasil!.IsActive);
        Assert.Null(hasil.DeactivatedAt);

        Assert.Equal(HttpStatusCode.OK,
            (await adminClient.GetAsync("/admin/sellers/pending")).StatusCode);
    }

    [Fact]
    public async Task Owner_tidak_bisa_menonaktifkan_dirinya_sendiri()
    {
        var ownerAuth = await api.StaffLoginAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var owner = api.ClientWithToken(ownerAuth.AccessToken);

        var response = await owner.PostAsJsonAsync($"/owner/admins/{ownerAuth.User.Id}/access",
            new SetAdminAccessRequest { Active = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/owner/settings")).StatusCode);
    }

    [Fact]
    public async Task Seller_tidak_bisa_dinonaktifkan_lewat_panel_admin()
    {
        var owner = await api.ClientAsOwnerAsync();
        var seller = await api.RegisterAsync(Roles.Seller);

        var response = await owner.PostAsJsonAsync($"/owner/admins/{seller.User.Id}/access",
            new SetAdminAccessRequest { Active = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mencabut_akun_yang_tidak_ada_menghasilkan_404()
    {
        var owner = await api.ClientAsOwnerAsync();

        var response = await owner.PostAsJsonAsync($"/owner/admins/{Guid.NewGuid()}/access",
            new SetAdminAccessRequest { Active = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
