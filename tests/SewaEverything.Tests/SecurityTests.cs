using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using SewaEverything.Contracts;
using SewaEverything.Infrastructure.Payments;

namespace SewaEverything.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Kunci#Rahasia#26")]
    [InlineData("gajah terbang di atas awan")]
    [InlineData("m4tahari-Terbit-9")]
    public void Sandi_yang_kuat_diterima(string sandi) =>
        Assert.Null(PasswordPolicy.Periksa(sandi));

    [Theory]
    [InlineData("Pendek#1", "minimal 12")]
    [InlineData("081234567890", "hanya berupa angka")]
    [InlineData("aaaaaaaaaaaaaa", "variasi")]
    [InlineData("abcdefghijkl", "berurutan")]
    [InlineData("qwertyuiop", "minimal 12")]
    [InlineData("passwordpassword", "sering dipakai")]
    public void Sandi_lemah_ditolak_dengan_alasan(string sandi, string potongan)
    {
        var galat = PasswordPolicy.Periksa(sandi);

        Assert.NotNull(galat);
        Assert.Contains(potongan, galat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sandi_tidak_boleh_memuat_bagian_email()
    {
        var galat = PasswordPolicy.Periksa("budisantoso#2026", email: "budisantoso@contoh.id");

        Assert.NotNull(galat);
        Assert.Contains("nama atau alamat email", galat);
    }

    [Fact]
    public void Sandi_tidak_boleh_memuat_nama()
    {
        var galat = PasswordPolicy.Periksa("rahasiaWidodo88", name: "Joko Widodo");

        Assert.NotNull(galat);
        Assert.Contains("nama atau alamat email", galat);
    }

    [Fact]
    public void Nama_terlalu_pendek_tidak_ikut_dilarang() =>
        Assert.Null(PasswordPolicy.Periksa("aliRahasia#2026", name: "Ali"));
}

[Collection(ApiCollection.Name)]
public sealed class SecurityHeaderTests(ApiFactory api)
{
    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "no-referrer")]
    public async Task Setiap_respons_membawa_header_pengaman(string nama, string nilai)
    {
        var response = await api.CreateClient().GetAsync("/items");

        Assert.True(response.Headers.TryGetValues(nama, out var isi), $"{nama} tidak ada");
        Assert.Equal(nilai, Assert.Single(isi!));
    }

    [Fact]
    public async Task Respons_membawa_content_security_policy_yang_mengunci_semuanya()
    {
        var response = await api.CreateClient().GetAsync("/items");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }
}

[Collection(ApiCollection.Name)]
public sealed class PasswordRegistrationTests(ApiFactory api)
{
    [Fact]
    public async Task Pendaftaran_menolak_sandi_lemah_dengan_penjelasan()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name     = "Penyewa Uji",
            Email    = ApiFactory.UniqueEmail("lemah"),
            Password = "rahasia123",
            Role     = "renter"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("minimal 12", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pendaftaran_menolak_sandi_yang_memuat_emailnya_sendiri()
    {
        var email = "wisnupradana@uji.local";

        var response = await api.CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name     = "Wisnu Pradana",
            Email    = email,
            Password = "wisnupradana2026",
            Role     = "renter"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nama atau alamat email", await response.Content.ReadAsStringAsync());
    }
}

[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(ApiFactory api)
{
    [Fact]
    public async Task Percobaan_masuk_berulang_dijawab_429()
    {
        using var terbatas = api.WithWebHostBuilder(b =>
        {
            b.UseSetting("Security:RateLimiting", "true");
            b.UseSetting("Security:LoginPerWindow", "3");
        });

        var client = terbatas.CreateClient();

        var permintaan = new LoginRequest
        {
            Email    = "tidak-ada@uji.local",
            Password = "SandiSalah#2026"
        };

        var status = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            status.Add((await client.PostAsJsonAsync("/auth/login", permintaan)).StatusCode);
        }

        Assert.Equal(3, status.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(2, status.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Jatah_minta_tautan_yang_habis_tidak_ikut_menutup_jalan_menyimpan_sandi_baru()
    {
        using var terbatas = api.WithWebHostBuilder(b =>
        {
            b.UseSetting("Security:RateLimiting", "true");
            b.UseSetting("Security:ForgotPasswordPerHour", "2");
            b.UseSetting("Security:ResetPasswordPerHour", "10");
        });

        var client = terbatas.CreateClient();

        var minta = new ForgotPasswordRequest { Email = "tidak-ada@uji.local" };
        var status = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            status.Add((await client.PostAsJsonAsync("/auth/forgot-password", minta)).StatusCode);
        }

        Assert.Equal(2, status.Count(s => s == HttpStatusCode.Accepted));
        Assert.Equal(2, status.Count(s => s == HttpStatusCode.TooManyRequests));

        var aturUlang = await client.PostAsJsonAsync("/auth/reset-password", new ResetPasswordRequest
        {
            Token    = "token-karangan",
            Password = "Kunci#Sementara#26"
        });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, aturUlang.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, aturUlang.StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class WebhookCredentialTests(ApiFactory api)
{
    private sealed class TanpaKunci : IMidtransCredentials
    {
        public ValueTask<MidtransOptions> CurrentAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(new MidtransOptions { ServerKey = string.Empty });
    }

    [Fact]
    public async Task Tanpa_kunci_server_notifikasi_ditolak_bukan_diterima()
    {
        using var tanpaKunci = api.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMidtransCredentials>();
            services.AddSingleton<IMidtransCredentials, TanpaKunci>();
        }));

        var payload = ApiFactory.MidtransNotification(
            PaymentLedger.OrderId(Guid.NewGuid(), 1), 250_000m, "settlement", serverKey: string.Empty);

        var response = await tanpaKunci.CreateClient().PostAsync("/webhooks/payment",
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
