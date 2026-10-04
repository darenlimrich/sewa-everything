using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class SellerVerificationTests(ApiFactory api)
{
    [Fact]
    public async Task Seller_baru_muncul_di_antrean_verifikasi()
    {
        var seller = await api.RegisterAsync(Roles.Seller);
        var admin = await api.ClientAsAdminAsync();

        var antrean = await admin.GetFromJsonAsync<List<PendingSellerResponse>>("/admin/sellers/pending");

        Assert.Contains(antrean!, s => s.Id == seller.User.Id);
    }

    [Fact]
    public async Task Admin_memverifikasi_seller_lalu_hilang_dari_antrean()
    {
        var seller = await api.RegisterAsync(Roles.Seller);
        var admin = await api.ClientAsAdminAsync();

        var response = await admin.PostAsJsonAsync(
            $"/admin/sellers/{seller.User.Id}/verify", new VerifySellerRequest { Approve = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var terverifikasi = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.True(terverifikasi!.IsVerified);

        var antrean = await admin.GetFromJsonAsync<List<PendingSellerResponse>>("/admin/sellers/pending");
        Assert.DoesNotContain(antrean!, s => s.Id == seller.User.Id);
    }

    [Fact]
    public async Task Seller_melihat_status_baru_tanpa_login_ulang()
    {
        var seller = await api.RegisterAsync(Roles.Seller);
        var sellerClient = api.ClientWithToken(seller.AccessToken);
        var admin = await api.ClientAsAdminAsync();

        var sebelum = await sellerClient.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.False(sebelum!.IsVerified);

        await admin.PostAsJsonAsync($"/admin/sellers/{seller.User.Id}/verify",
            new VerifySellerRequest { Approve = true });

        var sesudah = await sellerClient.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.True(sesudah!.IsVerified);
    }

    [Fact]
    public async Task Verifikasi_bisa_dicabut_kembali()
    {
        var seller = await api.RegisterAsync(Roles.Seller);
        var admin = await api.ClientAsAdminAsync();

        await admin.PostAsJsonAsync($"/admin/sellers/{seller.User.Id}/verify",
            new VerifySellerRequest { Approve = true });

        var cabut = await admin.PostAsJsonAsync($"/admin/sellers/{seller.User.Id}/verify",
            new VerifySellerRequest { Approve = false });

        var hasil = await cabut.Content.ReadFromJsonAsync<UserResponse>();
        Assert.False(hasil!.IsVerified);

        var antrean = await admin.GetFromJsonAsync<List<PendingSellerResponse>>("/admin/sellers/pending");
        Assert.Contains(antrean!, s => s.Id == seller.User.Id);
    }

    [Fact]
    public async Task Verifikasi_renter_ditolak()
    {
        var renter = await api.RegisterAsync(Roles.Renter);
        var admin = await api.ClientAsAdminAsync();

        var response = await admin.PostAsJsonAsync(
            $"/admin/sellers/{renter.User.Id}/verify", new VerifySellerRequest { Approve = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verifikasi_akun_tidak_ada_menghasilkan_404()
    {
        var admin = await api.ClientAsAdminAsync();

        var response = await admin.PostAsJsonAsync(
            $"/admin/sellers/{Guid.NewGuid()}/verify", new VerifySellerRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
