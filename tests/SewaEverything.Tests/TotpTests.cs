using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

public sealed class TotpAlgorithmTests
{
    private static byte[] Benih => Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Vektor_uji_rfc6238_cocok(long detik, string diharapkan)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(detik));

        Assert.Equal(diharapkan, Totp.Compute(Benih, step));
    }

    [Theory]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_cocok_dengan_rfc4648(string masukan, string diharapkan)
    {
        Assert.Equal(diharapkan, Base32.Encode(Encoding.ASCII.GetBytes(masukan)));
    }

    [Fact]
    public void Base32_bolak_balik_utuh()
    {
        var asli = Totp.NewSecret();

        Assert.True(Base32.TryDecode(Base32.Encode(asli), out var kembali));
        Assert.Equal(asli, kembali);
    }

    [Fact]
    public void Base32_memaafkan_spasi_dan_huruf_kecil()
    {
        Assert.True(Base32.TryDecode("mzxw 6ytb-oi", out var hasil));
        Assert.Equal("foobar", Encoding.ASCII.GetString(hasil));
    }

    [Fact]
    public void Base32_menolak_huruf_di_luar_abjadnya()
    {
        Assert.False(Base32.TryDecode("MZXW6YTB1", out _));
        Assert.False(Base32.TryDecode("", out _));
    }

    [Fact]
    public void Kode_di_luar_jendela_tidak_cocok()
    {
        var sekarang = Totp.StepAt(DateTimeOffset.UtcNow);
        var jauh = Totp.Compute(Benih, sekarang + 5);

        Assert.False(Totp.TryMatch(Benih, jauh, sekarang, window: 1, out _));
    }

    [Fact]
    public void Kode_satu_langkah_meleset_masih_diterima()
    {
        var sekarang = Totp.StepAt(DateTimeOffset.UtcNow);
        var sebelumnya = Totp.Compute(Benih, sekarang - 1);

        Assert.True(Totp.TryMatch(Benih, sebelumnya, sekarang, window: 1, out var cocok));
        Assert.Equal(sekarang - 1, cocok);
    }

    [Fact]
    public void Tautan_otpauth_membawa_algoritma_dan_periodenya()
    {
        var uri = Totp.BuildUri("Sewaku", "admin@contoh.local", "MZXW6YTBOI");

        Assert.StartsWith("otpauth://totp/Sewaku:admin%40contoh.local?", uri);
        Assert.Contains("secret=MZXW6YTBOI", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }
}

[Collection(ApiCollection.Name)]
public sealed class TotpTests(ApiFactory api)
{
    private static string KodeUntuk(string base32, long geser = 0)
    {
        Assert.True(Base32.TryDecode(base32, out var rahasia));

        return Totp.Compute(rahasia, Totp.StepAt(DateTimeOffset.UtcNow) + geser);
    }

    private async Task<(string Email, HttpClient Client)> AdminAsync()
    {
        var email = ApiFactory.UniqueEmail("staf2fa");

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            db.Users.Add(new User
            {
                Role         = UserRole.Admin,
                Name         = "Admin Dua Langkah",
                Email        = email,
                PasswordHash = hasher.Hash(ApiFactory.Password)
            });

            await db.SaveChangesAsync();
        }

        var auth = await api.StaffLoginAsync(email, ApiFactory.Password);

        return (email, api.ClientWithToken(auth.AccessToken));
    }

    private async Task<(string Email, HttpClient Client, string Secret, IReadOnlyList<string> Pemulihan)>
        AdminBerduaLangkahAsync()
    {
        var (email, client) = await AdminAsync();

        var mulai = await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        mulai.EnsureSuccessStatusCode();
        var daftar = (await mulai.Content.ReadFromJsonAsync<TotpSetupResponse>())!;

        var nyala = await client.PostAsJsonAsync("/auth/2fa/enable",
            new TotpCodeRequest { Code = KodeUntuk(daftar.Secret) });

        nyala.EnsureSuccessStatusCode();
        var hasil = (await nyala.Content.ReadFromJsonAsync<TotpEnableResponse>())!;

        return (email, client, daftar.Secret, hasil.RecoveryCodes);
    }

    private Task<HttpResponseMessage> MasukAsync(string email, string? kode = null) =>
        api.CreateClient().PostAsJsonAsync("/auth/staff/login", new LoginRequest
        {
            Email    = email,
            Password = ApiFactory.Password,
            TotpCode = kode
        });

    [Fact]
    public async Task Staf_menyalakan_dua_langkah_dan_menerima_sepuluh_kode_pemulihan()
    {
        var (_, _, _, pemulihan) = await AdminBerduaLangkahAsync();

        Assert.Equal(10, pemulihan.Count);
        Assert.Equal(10, pemulihan.Distinct().Count());
        Assert.All(pemulihan, k => Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", k));
    }

    [Fact]
    public async Task Rahasia_tidak_pernah_tersimpan_polos_di_database()
    {
        var (email, _, secret, _) = await AdminBerduaLangkahAsync();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var tersimpan = await db.UserTotps
            .Where(t => t.User!.Email == email.ToLower())
            .Select(t => t.Secret)
            .FirstAsync();

        Assert.StartsWith("enc.v1.", tersimpan);
        Assert.DoesNotContain(secret, tersimpan);
    }

    [Fact]
    public async Task Masuk_tanpa_kode_ditolak_dan_memberi_tahu_kode_diperlukan()
    {
        var (email, _, _, _) = await AdminBerduaLangkahAsync();

        var jawab = await MasukAsync(email);
        var badan = await jawab.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, jawab.StatusCode);
        Assert.Contains("\"totpRequired\":true", badan);
        Assert.Contains("Kode autentikasi diperlukan", badan);
    }

    [Fact]
    public async Task Kode_yang_benar_membuka_pintu_staf()
    {
        var (email, _, secret, _) = await AdminBerduaLangkahAsync();

        var jawab = await MasukAsync(email, KodeUntuk(secret, geser: 1));

        Assert.Equal(HttpStatusCode.OK, jawab.StatusCode);
        Assert.NotNull(await jawab.Content.ReadFromJsonAsync<AuthResponse>());
    }

    [Fact]
    public async Task Kode_yang_sama_tidak_dapat_dipakai_dua_kali()
    {
        var (email, _, secret, _) = await AdminBerduaLangkahAsync();

        var kode = KodeUntuk(secret, geser: 1);

        var pertama = await MasukAsync(email, kode);
        var kedua = await MasukAsync(email, kode);

        Assert.Equal(HttpStatusCode.OK, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, kedua.StatusCode);
        Assert.Contains("sudah pernah dipakai", await kedua.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Kode_salah_ikut_menghitung_ke_kunci_akun()
    {
        var (email, _, _, _) = await AdminBerduaLangkahAsync();

        var pertama = await MasukAsync(email, "000000");
        var kedua   = await MasukAsync(email, "000000");
        var ketiga  = await MasukAsync(email, "000000");

        Assert.Equal(HttpStatusCode.Unauthorized, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, kedua.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, ketiga.StatusCode);
    }

    [Fact]
    public async Task Kata_sandi_yang_benar_saja_tidak_cukup_lagi()
    {
        var (email, _, _, _) = await AdminBerduaLangkahAsync();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var gagal = await db.Users.Where(u => u.Email == email.ToLower())
            .Select(u => u.FailedLoginCount).FirstAsync();

        Assert.Equal(0, gagal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MasukAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Kode_pemulihan_bekerja_sekali_lalu_hangus()
    {
        var (email, _, _, pemulihan) = await AdminBerduaLangkahAsync();

        var pertama = await MasukAsync(email, pemulihan[0]);
        var kedua = await MasukAsync(email, pemulihan[0]);

        Assert.Equal(HttpStatusCode.OK, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, kedua.StatusCode);
    }

    [Fact]
    public async Task Kode_pemulihan_milik_akun_lain_tidak_membuka_apa_pun()
    {
        var (_, _, _, punyaOrangLain) = await AdminBerduaLangkahAsync();
        var (email, _, _, _) = await AdminBerduaLangkahAsync();

        var jawab = await MasukAsync(email, punyaOrangLain[0]);

        Assert.Equal(HttpStatusCode.Unauthorized, jawab.StatusCode);
    }

    [Fact]
    public async Task Kode_pemulihan_yang_terpakai_mengurangi_sisanya()
    {
        var (email, client, _, pemulihan) = await AdminBerduaLangkahAsync();

        await MasukAsync(email, pemulihan[0]);

        var status = await client.GetFromJsonAsync<TotpStatusResponse>("/auth/2fa");

        Assert.Equal(9, status!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Status_mengikuti_keadaan_sebenarnya()
    {
        var (_, client) = await AdminAsync();

        var awal = await client.GetFromJsonAsync<TotpStatusResponse>("/auth/2fa");

        Assert.False(awal!.Enabled);
        Assert.False(awal.PendingSetup);

        var mulai = await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        var daftar = (await mulai.Content.ReadFromJsonAsync<TotpSetupResponse>())!;
        var tengah = await client.GetFromJsonAsync<TotpStatusResponse>("/auth/2fa");

        Assert.False(tengah!.Enabled);
        Assert.True(tengah.PendingSetup);

        await client.PostAsJsonAsync("/auth/2fa/enable",
            new TotpCodeRequest { Code = KodeUntuk(daftar.Secret) });

        var akhir = await client.GetFromJsonAsync<TotpStatusResponse>("/auth/2fa");

        Assert.True(akhir!.Enabled);
        Assert.False(akhir.PendingSetup);
        Assert.NotNull(akhir.EnabledAt);
        Assert.Equal(10, akhir.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Pendaftaran_yang_belum_dikonfirmasi_tidak_menahan_siapa_pun()
    {
        var (email, client) = await AdminAsync();

        var mulai = await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        mulai.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await MasukAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Memulai_pendaftaran_menuntut_kata_sandi_yang_benar()
    {
        var (_, client) = await AdminAsync();

        var jawab = await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = "SandiYangSalah#2026" });

        Assert.Equal(HttpStatusCode.Unauthorized, jawab.StatusCode);
    }

    [Fact]
    public async Task Menyalakan_dengan_kode_salah_ditolak()
    {
        var (_, client) = await AdminAsync();

        await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        var jawab = await client.PostAsJsonAsync("/auth/2fa/enable",
            new TotpCodeRequest { Code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, jawab.StatusCode);
    }

    [Fact]
    public async Task Mendaftar_ulang_ditolak_selama_dua_langkah_masih_menyala()
    {
        var (_, client, _, _) = await AdminBerduaLangkahAsync();

        var jawab = await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.Conflict, jawab.StatusCode);
    }

    [Fact]
    public async Task Mematikan_menuntut_kata_sandi_dan_kode_sekaligus()
    {
        var (_, client, secret, _) = await AdminBerduaLangkahAsync();

        var tanpaKode = await client.PostAsJsonAsync("/auth/2fa/disable",
            new TotpDisableRequest { Password = ApiFactory.Password, Code = "000000" });

        var sandiSalah = await client.PostAsJsonAsync("/auth/2fa/disable",
            new TotpDisableRequest
            {
                Password = "SandiYangSalah#2026",
                Code = KodeUntuk(secret, geser: 1)
            });

        Assert.Equal(HttpStatusCode.BadRequest, tanpaKode.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sandiSalah.StatusCode);
    }

    [Fact]
    public async Task Mematikan_mengembalikan_masuk_tanpa_kode()
    {
        var (email, client, secret, _) = await AdminBerduaLangkahAsync();

        var mati = await client.PostAsJsonAsync("/auth/2fa/disable",
            new TotpDisableRequest
            {
                Password = ApiFactory.Password,
                Code = KodeUntuk(secret, geser: 1)
            });

        Assert.Equal(HttpStatusCode.NoContent, mati.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MasukAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Penyewa_tidak_dapat_menyentuh_endpoint_dua_langkah()
    {
        var penyewa = await api.ClientAsAsync(Roles.Renter);

        var status = await penyewa.GetAsync("/auth/2fa");

        var mulai = await penyewa.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, mulai.StatusCode);
    }

    [Fact]
    public async Task Akun_tanpa_dua_langkah_tidak_terpengaruh()
    {
        var email = (await api.RegisterAsync(Roles.Renter)).User.Email;

        var jawab = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = email, Password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, jawab.StatusCode);
    }

    [Fact]
    public async Task Owner_dapat_melepas_dua_langkah_admin_yang_kehilangan_ponselnya()
    {
        var (email, _, _, _) = await AdminBerduaLangkahAsync();

        Guid id;

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();
            id = await db.Users.Where(u => u.Email == email.ToLower()).Select(u => u.Id).FirstAsync();
        }

        var owner = await api.ClientAsOwnerAsync();
        var lepas = await owner.PostAsync($"/owner/admins/{id}/2fa/reset", null);

        Assert.Equal(HttpStatusCode.OK, lepas.StatusCode);

        var sesudah = (await lepas.Content.ReadFromJsonAsync<AdminAccountResponse>())!;

        Assert.False(sesudah.TwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, (await MasukAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Melepas_dua_langkah_ikut_membuang_kode_pemulihannya()
    {
        var (email, _, _, pemulihan) = await AdminBerduaLangkahAsync();

        Guid id;

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();
            id = await db.Users.Where(u => u.Email == email.ToLower()).Select(u => u.Id).FirstAsync();
        }

        var owner = await api.ClientAsOwnerAsync();
        await owner.PostAsync($"/owner/admins/{id}/2fa/reset", null);

        using var scope2 = api.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<SewaDbContext>();

        Assert.Equal(0, await db2.TotpRecoveryCodes.CountAsync(c => c.UserId == id));
        Assert.NotEmpty(pemulihan);
    }

    [Fact]
    public async Task Owner_tidak_dapat_melepas_dua_langkah_akun_yang_bukan_admin()
    {
        var penyewa = await api.RegisterAsync(Roles.Renter);
        var owner = await api.ClientAsOwnerAsync();

        var jawab = await owner.PostAsync($"/owner/admins/{penyewa.User.Id}/2fa/reset", null);

        Assert.Equal(HttpStatusCode.BadRequest, jawab.StatusCode);
    }

    [Fact]
    public async Task Melepas_dua_langkah_yang_tidak_menyala_ditolak()
    {
        var (email, _) = await AdminAsync();

        Guid id;

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();
            id = await db.Users.Where(u => u.Email == email.ToLower()).Select(u => u.Id).FirstAsync();
        }

        var owner = await api.ClientAsOwnerAsync();
        var jawab = await owner.PostAsync($"/owner/admins/{id}/2fa/reset", null);

        Assert.Equal(HttpStatusCode.Conflict, jawab.StatusCode);
    }

    [Fact]
    public async Task Daftar_admin_menyebutkan_siapa_yang_sudah_memasang_dua_langkah()
    {
        var (email, _, _, _) = await AdminBerduaLangkahAsync();
        var (tanpa, _) = await AdminAsync();

        var owner = await api.ClientAsOwnerAsync();
        var daftar = await owner.GetFromJsonAsync<List<AdminAccountResponse>>("/owner/admins") ?? [];

        Assert.True(daftar.Single(a => a.Email == email.ToLower()).TwoFactorEnabled);
        Assert.False(daftar.Single(a => a.Email == tanpa.ToLower()).TwoFactorEnabled);
    }

    private async Task<(string Email, HttpClient Client, Guid Id, string Secret, IReadOnlyList<string> Pemulihan)>
        PemilikBerduaLangkahAsync()
    {
        var auth = await api.RegisterAsync(Roles.Seller);
        var client = api.ClientWithToken(auth.AccessToken);

        var mulai = await client.PostAsJsonAsync("/auth/2fa/setup",
            new TotpSetupRequest { Password = ApiFactory.Password });

        mulai.EnsureSuccessStatusCode();
        var daftar = (await mulai.Content.ReadFromJsonAsync<TotpSetupResponse>())!;

        var nyala = await client.PostAsJsonAsync("/auth/2fa/enable",
            new TotpCodeRequest { Code = KodeUntuk(daftar.Secret) });

        nyala.EnsureSuccessStatusCode();
        var hasil = (await nyala.Content.ReadFromJsonAsync<TotpEnableResponse>())!;

        return (auth.User.Email, client, auth.User.Id, daftar.Secret, hasil.RecoveryCodes);
    }

    private Task<HttpResponseMessage> MasukPublikAsync(string email, string? kode = null) =>
        api.CreateClient().PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Email    = email,
            Password = ApiFactory.Password,
            TotpCode = kode
        });

    [Fact]
    public async Task Pemilik_barang_dapat_menyalakan_dua_langkah()
    {
        var (_, _, _, _, pemulihan) = await PemilikBerduaLangkahAsync();

        Assert.Equal(10, pemulihan.Count);
        Assert.All(pemulihan, k => Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", k));
    }

    [Fact]
    public async Task Pintu_publik_menuntut_kode_dari_pemilik_barang_ber_dua_langkah()
    {
        var (email, _, _, secret, _) = await PemilikBerduaLangkahAsync();

        var tanpa = await MasukPublikAsync(email);
        var badan = await tanpa.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, tanpa.StatusCode);
        Assert.Contains("\"totpRequired\":true", badan);

        var dengan = await MasukPublikAsync(email, KodeUntuk(secret, geser: 1));

        Assert.Equal(HttpStatusCode.OK, dengan.StatusCode);
    }
}
