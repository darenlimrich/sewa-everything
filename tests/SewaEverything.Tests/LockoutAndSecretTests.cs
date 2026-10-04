using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;
using SewaEverything.Infrastructure.Security;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class LoginLockoutTests(ApiFactory api)
{
    private const string SandiSalah = "SandiYangSalah#2026";

    private static LoginRequest Coba(string email, string sandi) =>
        new() { Email = email, Password = sandi };

    private async Task<int> HitunganAsync(string email)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        return await db.Users.Where(u => u.Email == email.ToLower())
            .Select(u => u.FailedLoginCount).FirstAsync();
    }

    [Fact]
    public async Task Salah_tiga_kali_mengunci_akun_sementara()
    {
        var email = (await api.RegisterAsync(Roles.Renter)).User.Email;
        var client = api.CreateClient();

        var pertama = await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah));
        var kedua   = await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah));
        var ketiga  = await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah));

        Assert.Equal(HttpStatusCode.Unauthorized, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, kedua.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, ketiga.StatusCode);

        Assert.Contains("dikunci sementara", await ketiga.Content.ReadAsStringAsync());
        Assert.NotNull(ketiga.Headers.RetryAfter);
    }

    [Fact]
    public async Task Selama_terkunci_sandi_yang_benar_pun_ditolak()
    {
        var email = (await api.RegisterAsync(Roles.Renter)).User.Email;
        var client = api.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah));
        }

        var benar = await client.PostAsJsonAsync("/auth/login", Coba(email, ApiFactory.Password));

        Assert.Equal(HttpStatusCode.Locked, benar.StatusCode);
    }

    [Fact]
    public async Task Sandi_benar_sebelum_ambang_membersihkan_penghitung()
    {
        var email = (await api.RegisterAsync(Roles.Renter)).User.Email;
        var client = api.CreateClient();

        await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah));
        await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah));

        Assert.Equal(2, await HitunganAsync(email));

        var berhasil = await client.PostAsJsonAsync("/auth/login", Coba(email, ApiFactory.Password));

        Assert.Equal(HttpStatusCode.OK, berhasil.StatusCode);
        Assert.Equal(0, await HitunganAsync(email));
    }

    [Fact]
    public async Task Pintu_staf_ikut_mengunci()
    {
        var client = api.CreateClient();

        var status = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            status.Add((await client.PostAsJsonAsync(
                "/auth/staff/login", Coba(ApiFactory.OwnerEmail, SandiSalah))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Locked, status[^1]);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE users
               SET failed_login_count = 0, last_failed_login_at = NULL, locked_until = NULL
             WHERE email = {0}
            """, ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task Email_tak_terdaftar_tidak_pernah_mengunci_apa_pun()
    {
        var client = api.CreateClient();
        var email = ApiFactory.UniqueEmail("hantu");

        var status = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            status.Add((await client.PostAsJsonAsync("/auth/login", Coba(email, SandiSalah))).StatusCode);
        }

        Assert.All(status, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
    }
}

public class SecretProtectorTests
{
    private static AesGcmSecretProtector Pelindung(string? kunci = ApiFactory.SecretKey) =>
        new(Options.Create(new SecretProtectionOptions { SecretKey = kunci }));

    [Fact]
    public void Terenkripsi_lalu_dibuka_kembali_utuh()
    {
        var pelindung = Pelindung();
        var rahasia = "Mid-server-RAHASIA-2026";

        var tersimpan = pelindung.Protect(rahasia);

        Assert.NotEqual(rahasia, tersimpan);
        Assert.DoesNotContain(rahasia, tersimpan, StringComparison.Ordinal);
        Assert.True(pelindung.IsProtected(tersimpan));
        Assert.Equal(rahasia, pelindung.Reveal(tersimpan));
    }

    [Fact]
    public void Dua_enkripsi_atas_teks_sama_menghasilkan_ciphertext_berbeda()
    {
        var pelindung = Pelindung();

        Assert.NotEqual(pelindung.Protect("sama"), pelindung.Protect("sama"));
    }

    [Fact]
    public void Ciphertext_yang_diutak_atik_ditolak()
    {
        var pelindung = Pelindung();
        var tersimpan = pelindung.Protect("Mid-server-RAHASIA-2026");

        var rusak = tersimpan[..^6] + (tersimpan[^6] == 'A' ? "BBBBB" : "AAAAA") + tersimpan[^1];

        Assert.ThrowsAny<Exception>(() => pelindung.Reveal(rusak));
    }

    [Fact]
    public void Nilai_polos_lama_tetap_terbaca_apa_adanya()
    {
        var pelindung = Pelindung();

        Assert.Equal("SB-Mid-server-LAMA", pelindung.Reveal("SB-Mid-server-LAMA"));
        Assert.False(pelindung.IsProtected("SB-Mid-server-LAMA"));
    }

    [Fact]
    public void Tanpa_kunci_tidak_dapat_mengenkripsi()
    {
        var pelindung = Pelindung(null);

        Assert.False(pelindung.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => pelindung.Protect("apa pun"));
    }

    [Fact]
    public void Tanpa_kunci_nilai_terenkripsi_tidak_dapat_dibuka_diam_diam()
    {
        var tersimpan = Pelindung().Protect("Mid-server-RAHASIA-2026");

        Assert.Throws<InvalidOperationException>(() => Pelindung(null).Reveal(tersimpan));
    }

    [Theory]
    [InlineData("bukan-base64!!")]
    [InlineData("dGVybGFsdS1wZW5kZWs=")]
    public void Kunci_yang_bentuknya_salah_menggagalkan_startup(string kunci) =>
        Assert.Throws<InvalidOperationException>(() => Pelindung(kunci));
}

[Collection(ApiCollection.Name)]
public sealed class StoredSecretTests(ApiFactory api)
{
    [Fact]
    public async Task Server_key_tersimpan_terenkripsi_di_database_dan_tetap_terpakai()
    {
        const string kunci = "SB-Mid-server-DIENKRIPSI-2026";

        try
        {
            var owner = await api.ClientAsOwnerAsync();

            var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
                new UpdatePaymentGatewayRequest
                {
                    ServerKey    = kunci,
                    ClientKey    = "SB-Mid-client-DIENKRIPSI",
                    IsProduction = false
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

            var mentah = await db.PlatformSettings.AsNoTracking()
                .Select(s => s.MidtransServerKey).FirstAsync();

            Assert.NotNull(mentah);
            Assert.StartsWith(AesGcmSecretProtector.Prefix, mentah);
            Assert.DoesNotContain(kunci, mentah, StringComparison.Ordinal);

            var kredensial = scope.ServiceProvider.GetRequiredService<
                SewaEverything.Infrastructure.Payments.IMidtransCredentials>();

            Assert.Equal(kunci, (await kredensial.CurrentAsync()).ServerKey);

            var terlihat = await owner.GetFromJsonAsync<PaymentGatewayResponse>("/owner/payment-gateway");

            Assert.Equal($"••••{kunci[^4..]}", terlihat!.ServerKeyHint);
        }
        finally
        {
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

            await db.Database.ExecuteSqlRawAsync("""
                UPDATE platform_settings
                   SET midtrans_server_key = NULL,
                       midtrans_client_key = NULL,
                       midtrans_is_production = false
                """);
        }
    }
}
