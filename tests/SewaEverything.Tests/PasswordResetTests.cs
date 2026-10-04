using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class PasswordResetTests(ApiFactory api)
{
    private const string SandiBaru = "Rahasia#Sekali#26";

    private Task<HttpResponseMessage> MintaAsync(string email) =>
        api.CreateClient().PostAsJsonAsync("/auth/forgot-password",
            new ForgotPasswordRequest { Email = email });

    private Task<HttpResponseMessage> AturUlangAsync(string token, string password) =>
        api.CreateClient().PostAsJsonAsync("/auth/reset-password",
            new ResetPasswordRequest { Token = token, Password = password });

    private Task<HttpResponseMessage> MasukAsync(string email, string password) =>
        api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = email, Password = password });

    private async Task<(string Email, Guid Id, string Token)> PenyewaLupaSandiAsync()
    {
        var email = ApiFactory.UniqueEmail("lupa");
        var akun = await api.RegisterAsync(Roles.Renter, email);

        api.Email.Clear();

        var minta = await MintaAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, minta.StatusCode);

        return (email, akun.User.Id, api.Email.TokenFor(email));
    }

    [Fact]
    public async Task Email_terdaftar_menerima_tautan_yang_menyebut_masa_berlakunya()
    {
        var (email, _, token) = await PenyewaLupaSandiAsync();

        var surel = api.Email.LastFor(email);

        Assert.Equal("Atur ulang kata sandi Sewaku", surel.Subject);
        Assert.Contains("/atur-ulang?token=", surel.TextBody);
        Assert.Contains("60 menit", surel.TextBody);
        Assert.Contains("/atur-ulang?token=", surel.HtmlBody);
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task Email_tak_terdaftar_dijawab_persis_sama_dan_tidak_mengirim_apa_pun()
    {
        var terdaftar = ApiFactory.UniqueEmail("ada");
        await api.RegisterAsync(Roles.Renter, terdaftar);

        var asing = ApiFactory.UniqueEmail("tidak-ada");

        api.Email.Clear();

        var jawabanAda = await MintaAsync(terdaftar);
        var jawabanAsing = await MintaAsync(asing);

        Assert.Equal(jawabanAda.StatusCode, jawabanAsing.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, jawabanAsing.StatusCode);

        Assert.Equal(
            await jawabanAda.Content.ReadAsStringAsync(),
            await jawabanAsing.Content.ReadAsStringAsync());

        Assert.Empty(api.Email.For(asing));
        Assert.Single(api.Email.For(terdaftar));
    }

    [Fact]
    public async Task Tautan_mengganti_sandi_dan_sandi_lama_langsung_mati()
    {
        var (email, _, token) = await PenyewaLupaSandiAsync();

        var atur = await AturUlangAsync(token, SandiBaru);
        Assert.Equal(HttpStatusCode.NoContent, atur.StatusCode);

        var lama = await MasukAsync(email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, lama.StatusCode);

        var baru = await MasukAsync(email, SandiBaru);
        Assert.Equal(HttpStatusCode.OK, baru.StatusCode);
    }

    [Fact]
    public async Task Tautan_yang_sama_tidak_dapat_dipakai_dua_kali()
    {
        var (_, _, token) = await PenyewaLupaSandiAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await AturUlangAsync(token, SandiBaru)).StatusCode);

        var kedua = await AturUlangAsync(token, "Sandi#Lain#2026");

        Assert.Equal(HttpStatusCode.Gone, kedua.StatusCode);
    }

    [Fact]
    public async Task Tautan_lama_yang_belum_dipakai_ikut_mati_setelah_reset_berhasil()
    {
        var email = ApiFactory.UniqueEmail("lupa-dua");
        await api.RegisterAsync(Roles.Renter, email);

        api.Email.Clear();
        await MintaAsync(email);

        var tokenLama = api.Email.TokenFor(email);
        var tokenKedua = await TerbitkanTokenLangsungAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await AturUlangAsync(tokenKedua, SandiBaru)).StatusCode);

        var pakaiYangLama = await AturUlangAsync(tokenLama, "Sandi#Ketiga#26");

        Assert.Equal(HttpStatusCode.Gone, pakaiYangLama.StatusCode);
    }

    [Fact]
    public async Task Tautan_kedaluwarsa_ditolak()
    {
        var email = ApiFactory.UniqueEmail("kedaluwarsa");
        await api.RegisterAsync(Roles.Renter, email);

        var token = await TerbitkanTokenLangsungAsync(email, umur: TimeSpan.FromHours(-1));

        var jawaban = await AturUlangAsync(token, SandiBaru);

        Assert.Equal(HttpStatusCode.Gone, jawaban.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MasukAsync(email, ApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task Token_karangan_ditolak_tanpa_membocorkan_apa_pun()
    {
        var jawaban = await AturUlangAsync("token-yang-tidak-pernah-diterbitkan", SandiBaru);

        Assert.Equal(HttpStatusCode.Gone, jawaban.StatusCode);
    }

    [Fact]
    public async Task Sandi_lemah_ditolak_dan_tautannya_tetap_berlaku()
    {
        var (email, _, token) = await PenyewaLupaSandiAsync();

        var lemah = await AturUlangAsync(token, "12345678");
        Assert.Equal(HttpStatusCode.BadRequest, lemah.StatusCode);

        var kuat = await AturUlangAsync(token, SandiBaru);
        Assert.Equal(HttpStatusCode.NoContent, kuat.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await MasukAsync(email, SandiBaru)).StatusCode);
    }

    [Fact]
    public async Task Sandi_baru_tidak_boleh_memuat_nama_atau_email_pemiliknya()
    {
        const string email = "bagaskara@test.local";
        await api.RegisterAsync(Roles.Renter, email);

        api.Email.Clear();
        await MintaAsync(email);

        var token = api.Email.TokenFor(email);

        var jawaban = await AturUlangAsync(token, "bagaskara#Rahasia26");

        Assert.Equal(HttpStatusCode.BadRequest, jawaban.StatusCode);
        Assert.Contains("nama atau alamat email", await jawaban.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_mencabut_seluruh_sesi_yang_masih_hidup()
    {
        var email = ApiFactory.UniqueEmail("sesi");
        var sesiLama = await api.RegisterAsync(Roles.Renter, email);

        api.Email.Clear();
        await MintaAsync(email);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await AturUlangAsync(api.Email.TokenFor(email), SandiBaru)).StatusCode);

        var perpanjang = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = sesiLama.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, perpanjang.StatusCode);
    }

    [Fact]
    public async Task Akun_yang_terkunci_salah_sandi_terbuka_lagi_setelah_reset()
    {
        var email = ApiFactory.UniqueEmail("terkunci");
        await api.RegisterAsync(Roles.Renter, email);

        for (var i = 0; i < 3; i++)
        {
            await MasukAsync(email, "SandiSalah#2026");
        }

        var terkunci = await MasukAsync(email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.Locked, terkunci.StatusCode);

        var token = await TerbitkanTokenLangsungAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await AturUlangAsync(token, SandiBaru)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MasukAsync(email, SandiBaru)).StatusCode);
    }

    [Fact]
    public async Task Permintaan_beruntun_tidak_membanjiri_kotak_masuk()
    {
        var email = ApiFactory.UniqueEmail("banjir");
        await api.RegisterAsync(Roles.Renter, email);

        api.Email.Clear();

        var pertama = await MintaAsync(email);
        var kedua = await MintaAsync(email);
        var ketiga = await MintaAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, kedua.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, ketiga.StatusCode);

        Assert.Single(api.Email.For(email));
    }

    [Fact]
    public async Task Akun_yang_aksesnya_dicabut_tidak_menerima_tautan()
    {
        var owner = await api.ClientAsOwnerAsync();

        var email = ApiFactory.UniqueEmail("dicabut");

        var dibuat = await (await owner.PostAsJsonAsync("/owner/admins", new CreateAdminRequest
        {
            Name     = "Admin Dicabut",
            Email    = email,
            Password = ApiFactory.Password
        })).Content.ReadFromJsonAsync<AdminAccountResponse>();

        var cabut = await owner.PostAsJsonAsync($"/owner/admins/{dibuat!.Id}/access",
            new SetAdminAccessRequest { Active = false });

        cabut.EnsureSuccessStatusCode();

        api.Email.Clear();

        var jawaban = await MintaAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, jawaban.StatusCode);
        Assert.Empty(api.Email.For(email));
    }

    [Fact]
    public async Task Staf_ikut_dapat_memulihkan_sandinya_sendiri()
    {
        var owner = await api.ClientAsOwnerAsync();

        var email = ApiFactory.UniqueEmail("admin-lupa");

        var dibuat = await owner.PostAsJsonAsync("/owner/admins", new CreateAdminRequest
        {
            Name     = "Admin Lupa",
            Email    = email,
            Password = ApiFactory.Password
        });

        dibuat.EnsureSuccessStatusCode();

        api.Email.Clear();
        await MintaAsync(email);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await AturUlangAsync(api.Email.TokenFor(email), SandiBaru)).StatusCode);

        var pintuStaf = await api.CreateClient().PostAsJsonAsync("/auth/staff/login",
            new LoginRequest { Email = email, Password = SandiBaru });

        Assert.Equal(HttpStatusCode.OK, pintuStaf.StatusCode);

        var pintuPublik = await MasukAsync(email, SandiBaru);

        Assert.Equal(HttpStatusCode.Unauthorized, pintuPublik.StatusCode);
    }

    [Fact]
    public async Task Token_mentah_tidak_pernah_tersimpan_di_database()
    {
        var (email, id, token) = await PenyewaLupaSandiAsync();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var baris = await db.PasswordResets.AsNoTracking()
            .Where(r => r.UserId == id)
            .SingleAsync();

        Assert.Equal(32, baris.TokenHash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), baris.TokenHash);
        Assert.Null(baris.UsedAt);
        Assert.NotEqual(token, Encoding.UTF8.GetString(baris.TokenHash));
        Assert.NotEmpty(email);
    }

    [Fact]
    public async Task Surel_yang_gagal_terkirim_tidak_mengubah_jawaban_ke_pengguna()
    {
        var email = ApiFactory.UniqueEmail("smtp-mati");
        await api.RegisterAsync(Roles.Renter, email);

        api.Email.Clear();
        api.Email.FailWith = new InvalidOperationException("SMTP menolak koneksi.");

        try
        {
            var jawaban = await MintaAsync(email);

            Assert.Equal(HttpStatusCode.Accepted, jawaban.StatusCode);
        }
        finally
        {
            api.Email.FailWith = null;
        }
    }

    private async Task<string> TerbitkanTokenLangsungAsync(string email, TimeSpan? umur = null)
    {
        var token = $"uji-{Guid.NewGuid():N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var berlaku = umur ?? TimeSpan.FromHours(1);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO password_resets (user_id, token_hash, created_at, expires_at)
            VALUES ({userId}, {hash}, now() - interval '2 hours', now() + {berlaku})
            """);

        return token;
    }
}
