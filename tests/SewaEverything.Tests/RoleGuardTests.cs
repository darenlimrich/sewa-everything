using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class RoleGuardTests(ApiFactory api)
{

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Endpoint_admin_menolak_role_biasa(string role)
    {
        var client = await api.ClientAsAsync(role);

        var response = await client.GetAsync("/admin/sellers/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Verifikasi_seller_menolak_role_biasa(string role)
    {
        var client = await api.ClientAsAsync(role);

        var response = await client.PostAsJsonAsync(
            $"/admin/sellers/{Guid.NewGuid()}/verify", new VerifySellerRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_admin_menerima_admin()
    {
        var client = await api.ClientAsAdminAsync();

        var response = await client.GetAsync("/admin/sellers/pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/sellers/pending")]
    [InlineData("/admin/sellers")]
    [InlineData("/admin/items")]
    [InlineData("/admin/disputes")]
    [InlineData("/admin/payouts/pending")]
    public async Task Antrean_admin_menolak_owner(string path)
    {
        var client = await api.ClientAsOwnerAsync();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Aksi_antrean_admin_menolak_owner()
    {
        var client = await api.ClientAsOwnerAsync();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/admin/sellers/{id}/verify", new VerifySellerRequest())).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/admin/items/{id}/suspend", new SuspendItemRequest { Reason = "Uji" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/admin/items/{id}/unsuspend", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/admin/disputes/{id}/resolve", new ResolveDisputeRequest
            {
                DepositDeduction = 0m,
                Resolution       = "Uji"
            })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/admin/payouts/{id}/settle", new { })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    [InlineData(Roles.Seller)]
    public async Task Setelan_platform_menolak_role_biasa(string role)
    {
        var client = await api.ClientAsAsync(role);

        var response = await client.GetAsync("/owner/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Setelan_platform_menolak_admin()
    {
        var client = await api.ClientAsAdminAsync();

        var baca = await client.GetAsync("/owner/settings");
        var tulis = await client.PutAsJsonAsync("/owner/settings", new UpdatePlatformSettingsRequest
        {
            CommissionRate  = 0.99m,
            CommissionMode  = CommissionModes.Deduct,
            ApprovalMinutes = 1440,
            PaymentMinutes  = 60
        });

        Assert.Equal(HttpStatusCode.Forbidden, baca.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, tulis.StatusCode);
    }

    [Fact]
    public async Task Setelan_platform_menerima_owner()
    {
        var client = await api.ClientAsOwnerAsync();

        var response = await client.GetAsync("/owner/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    public async Task Pasang_listing_menolak_non_seller(string role)
    {
        var client = await api.ClientAsAsync(role);

        var response = await client.PostAsJsonAsync("/items", new CreateItemRequest
        {
            Title         = "Barang Selundupan",
            Category      = "Lainnya",
            Price         = 10_000m,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = 0m
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pasang_listing_menolak_admin_dan_owner()
    {
        var admin = await api.ClientAsAdminAsync();
        var owner = await api.ClientAsOwnerAsync();

        var request = new CreateItemRequest
        {
            Title         = "Barang Staf",
            Category      = "Lainnya",
            Price         = 10_000m,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = 0m
        };

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync("/items", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PostAsJsonAsync("/items", request)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Renter)]
    public async Task Katalog_sendiri_menolak_non_seller(string role)
    {
        var client = await api.ClientAsAsync(role);

        var response = await client.GetAsync("/items/mine");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Blackout_menolak_renter()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var renter = await api.ClientAsAsync(Roles.Renter);

        var response = await renter.PostAsJsonAsync($"/items/{item.Id}/blackouts",
            new CreateBlackoutRequest
            {
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                EndsAt   = DateTimeOffset.UtcNow.AddDays(2)
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Katalog_terbuka_untuk_umum_tapi_menulis_tetap_perlu_token()
    {
        var seller = await api.VerifiedSellerAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var anonim = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonim.GetAsync("/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonim.GetAsync($"/items/{item.Id}")).StatusCode);

        var pasang = await anonim.PostAsJsonAsync("/items", new CreateItemRequest
        {
            Title         = "Tanpa Token",
            Category      = "Lainnya",
            Price         = 10_000m,
            PriceUnit     = PriceUnits.Day,
            DepositAmount = 0m
        });

        Assert.Equal(HttpStatusCode.Unauthorized, pasang.StatusCode);
    }

    [Theory]
    [InlineData("/admin/sellers/pending")]
    [InlineData("/owner/settings")]
    [InlineData("/auth/me")]
    [InlineData("/items/mine")]
    public async Task Tanpa_token_selalu_401(string path)
    {
        var response = await api.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_dengan_tanda_tangan_palsu_ditolak()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var bagian = auth.AccessToken.Split('.');
        bagian[2] = bagian[2][0] == 'A' ? 'B' + bagian[2][1..] : 'A' + bagian[2][1..];

        var client = api.ClientWithToken(string.Join('.', bagian));

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Header_role_buatan_klien_diabaikan()
    {
        var client = await api.ClientAsAsync(Roles.Renter);
        client.DefaultRequestHeaders.Add("X-Role", Roles.Owner);
        client.DefaultRequestHeaders.Add("role", Roles.Admin);

        var response = await client.GetAsync("/admin/sellers/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
