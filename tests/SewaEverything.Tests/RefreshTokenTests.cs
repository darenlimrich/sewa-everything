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
public sealed class RefreshTokenTests(ApiFactory api)
{

    [Fact]
    public async Task Login_membawa_refresh_token_beserta_masa_berlakunya()
    {
        var auth = await api.StaffLoginAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));

        Assert.NotEqual(auth.AccessToken, auth.RefreshToken);

        Assert.InRange(auth.RefreshTokenExpiresAt,
            DateTime.UtcNow.AddDays(30).AddMinutes(-5),
            DateTime.UtcNow.AddDays(30).AddMinutes(5));
    }

    [Fact]
    public async Task Register_juga_langsung_membawa_sesi_yang_bisa_diperpanjang()
    {
        var auth = await api.RegisterAsync(Roles.Renter);

        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));

        var refreshed = await RefreshSuccessfullyAsync(auth.RefreshToken);
        Assert.Equal(auth.User.Id, refreshed.User.Id);
    }

    [Fact]
    public async Task Yang_tersimpan_di_database_cuma_hash_tokennya()
    {
        var auth = await api.RegisterAsync(Roles.Renter);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(auth.RefreshToken));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var tersimpan = await db.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.UserId == auth.User.Id);

        Assert.Equal(hash, tersimpan.TokenHash);
        Assert.Equal(32, tersimpan.TokenHash.Length);
    }

    [Fact]
    public async Task Menukar_refresh_token_menghasilkan_pasangan_token_baru_yang_dipakai_server()
    {
        var awal = await api.RegisterAsync(Roles.Renter);

        var baru = await RefreshSuccessfullyAsync(awal.RefreshToken);

        Assert.NotEqual(awal.RefreshToken, baru.RefreshToken);
        Assert.Equal(awal.User.Id, baru.User.Id);

        var me = await api.ClientWithToken(baru.AccessToken).GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.Equal(awal.User.Id, me!.Id);

        Assert.InRange(baru.RefreshTokenExpiresAt,
            DateTime.UtcNow.AddDays(30).AddMinutes(-5),
            DateTime.UtcNow.AddDays(30).AddMinutes(5));
    }

    [Fact]
    public async Task Token_yang_sudah_ditukar_tidak_laku_lagi()
    {
        var awal = await api.RegisterAsync(Roles.Renter);
        await RefreshSuccessfullyAsync(awal.RefreshToken);

        var ulang = await RefreshAsync(awal.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, ulang.StatusCode);
    }

    [Fact]
    public async Task Pemakaian_ulang_mengakhiri_seluruh_sesi_bukan_cuma_token_itu()
    {
        var awal = await api.RegisterAsync(Roles.Renter);
        var kedua = await RefreshSuccessfullyAsync(awal.RefreshToken);

        var ulang = await RefreshAsync(awal.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, ulang.StatusCode);

        var lanjut = await RefreshAsync(kedua.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, lanjut.StatusCode);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var alasan = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == awal.User.Id)
            .Select(t => t.RevokedReason)
            .ToListAsync();

        Assert.Equal(2, alasan.Count);
        Assert.All(alasan, a => Assert.Equal(RefreshTokenRevokeReasons.ReuseDetected, a));
    }

    [Fact]
    public async Task Sesi_perangkat_lain_tidak_ikut_mati()
    {
        var akun = await api.RegisterAsync(Roles.Renter);
        var email = akun.User.Email;

        var ponsel = await api.LoginAsync(email, ApiFactory.Password);
        var tablet = await api.LoginAsync(email, ApiFactory.Password);

        await RefreshSuccessfullyAsync(tablet.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tablet.RefreshToken)).StatusCode);

        var lanjut = await RefreshSuccessfullyAsync(ponsel.RefreshToken);
        Assert.Equal(akun.User.Id, lanjut.User.Id);
    }

    [Theory]
    [InlineData("bukan-token-siapa-siapa")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Token_karangan_ditolak(string karangan)
    {
        var response = await RefreshAsync(karangan);

        Assert.Contains(response.StatusCode,
            (HttpStatusCode[])[HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest]);
    }

    [Fact]
    public async Task Refresh_token_yang_kedaluwarsa_ditolak()
    {
        var akun = await api.RegisterAsync(Roles.Renter);
        var kedaluwarsa = await SeedExpiredRefreshTokenAsync(akun.User.Id);

        var response = await RefreshAsync(kedaluwarsa);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Penukaran_membawa_profil_terbaru()
    {
        var auth = await api.RegisterAsync(Roles.Seller);
        Assert.False(auth.User.IsVerified);

        await api.VerifySellerAsync(auth.User.Id);

        var baru = await RefreshSuccessfullyAsync(auth.RefreshToken);

        Assert.True(baru.User.IsVerified);
    }

    [Fact]
    public async Task Akun_yang_dicabut_tidak_bisa_memperpanjang_sesinya()
    {
        var akun = await api.RegisterAsync(Roles.Seller);

        await SetAccountActiveAsync(akun.User.Id, active: false);

        var ditolak = await RefreshAsync(akun.RefreshToken);
        Assert.Equal(HttpStatusCode.Forbidden, ditolak.StatusCode);

        await SetAccountActiveAsync(akun.User.Id, active: true);

        var lanjut = await RefreshSuccessfullyAsync(akun.RefreshToken);
        Assert.Equal(akun.User.Id, lanjut.User.Id);
    }

    [Fact]
    public async Task Keluar_mengakhiri_kemampuan_memperpanjang()
    {
        var akun = await api.RegisterAsync(Roles.Renter);

        var keluar = await LogoutAsync(akun.RefreshToken);
        Assert.Equal(HttpStatusCode.NoContent, keluar.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(akun.RefreshToken)).StatusCode);

        var me = await api.ClientWithToken(akun.AccessToken).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Keluar_dengan_token_tak_dikenal_tetap_dijawab_sukses()
    {
        var response = await LogoutAsync("token-yang-tidak-pernah-ada");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Keluar_di_satu_perangkat_tidak_mengeluarkan_perangkat_lain()
    {
        var akun = await api.RegisterAsync(Roles.Renter);
        var tablet = await api.LoginAsync(akun.User.Email, ApiFactory.Password);

        (await LogoutAsync(tablet.RefreshToken)).EnsureSuccessStatusCode();

        var lanjut = await RefreshSuccessfullyAsync(akun.RefreshToken);
        Assert.Equal(akun.User.Id, lanjut.User.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Lima_penukaran_paralel_hanya_satu_yang_berhasil(int iterasi)
    {
        var akun = await api.RegisterAsync(Roles.Renter, email: ApiFactory.UniqueEmail($"balapan-{iterasi}"));

        var klien = Enumerable.Range(0, 5).Select(_ => api.CreateClient()).ToArray();

        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var upaya = klien.Select(c => Task.Run(async () =>
        {
            await gerbang.Task;
            return await c.PostAsJsonAsync("/auth/refresh",
                new RefreshTokenRequest { RefreshToken = akun.RefreshToken });
        })).ToArray();

        gerbang.SetResult();

        var hasil = await Task.WhenAll(upaya);

        Assert.Equal(1, hasil.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, hasil.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var terpakai = await db.RefreshTokens.AsNoTracking()
            .CountAsync(t => t.UserId == akun.User.Id && t.UsedAt != null);

        Assert.Equal(1, terpakai);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = refreshToken });

    private Task<HttpResponseMessage> LogoutAsync(string refreshToken) =>
        api.CreateClient().PostAsJsonAsync("/auth/logout",
            new RefreshTokenRequest { RefreshToken = refreshToken });

    private async Task<AuthResponse> RefreshSuccessfullyAsync(string refreshToken)
    {
        var response = await RefreshAsync(refreshToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> SeedExpiredRefreshTokenAsync(Guid userId)
    {
        var value = $"token-kedaluwarsa-{Guid.NewGuid():N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO refresh_tokens (user_id, token_hash, family_id, issued_at, expires_at)
            VALUES ({userId}, {hash}, gen_random_uuid(),
                    now() - interval '40 days', now() - interval '10 days')
            """);

        return value;
    }

    private async Task SetAccountActiveAsync(Guid userId, bool active)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var user = await db.Users.FirstAsync(u => u.Id == userId);

        if (active)
        {
            user.Reactivate();
        }
        else
        {
            user.Deactivate(DateTime.UtcNow);
        }

        await db.SaveChangesAsync();
    }

    private async Task<int> BarisTokenAsync(Guid userId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        return await db.RefreshTokens.AsNoTracking().CountAsync(t => t.UserId == userId);
    }

    private async Task SisipkanTokenKedaluwarsaAsync(Guid userId, int kedaluwarsaHariLalu)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var expires = DateTime.UtcNow.AddDays(-kedaluwarsaHariLalu);
        var issued = expires.AddDays(-30);
        var hash = SHA256.HashData(Guid.NewGuid().ToByteArray());

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO refresh_tokens
                (user_id, token_hash, family_id, issued_at, expires_at, used_at)
            VALUES ({userId}, {hash}, {Guid.NewGuid()}, {issued}, {expires}, {expires})
            """);
    }

    [Fact]
    public async Task Baris_mati_yang_lewat_masa_simpan_dibuang()
    {
        var akun = await api.RegisterAsync(Roles.Renter, email: ApiFactory.UniqueEmail("sapu-lama"));

        await SisipkanTokenKedaluwarsaAsync(akun.User.Id, kedaluwarsaHariLalu: 400);
        await SisipkanTokenKedaluwarsaAsync(akun.User.Id, kedaluwarsaHariLalu: 400);

        Assert.Equal(3, await BarisTokenAsync(akun.User.Id));

        await api.SweepRefreshTokensAsync();

        Assert.Equal(1, await BarisTokenAsync(akun.User.Id));
    }

    [Fact]
    public async Task Baris_mati_yang_belum_lewat_masa_simpan_tidak_disentuh()
    {
        var akun = await api.RegisterAsync(Roles.Renter, email: ApiFactory.UniqueEmail("sapu-baru"));

        await SisipkanTokenKedaluwarsaAsync(akun.User.Id, kedaluwarsaHariLalu: 3);

        await api.SweepRefreshTokensAsync();

        Assert.Equal(2, await BarisTokenAsync(akun.User.Id));
    }

    [Fact]
    public async Task Sapuan_tidak_mematikan_deteksi_pemakaian_ulang()
    {
        var akun = await api.RegisterAsync(Roles.Renter, email: ApiFactory.UniqueEmail("sapu-ulang"));

        var rotasi = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = akun.RefreshToken });

        rotasi.EnsureSuccessStatusCode();
        var baru = (await rotasi.Content.ReadFromJsonAsync<AuthResponse>())!;

        await api.SweepRefreshTokensAsync();

        var diputarUlang = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = akun.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, diputarUlang.StatusCode);

        var sesudahnya = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = baru.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, sesudahnya.StatusCode);
    }

    [Fact]
    public async Task Sesi_hidup_tetap_bisa_diperpanjang_setelah_sapuan()
    {
        var akun = await api.RegisterAsync(Roles.Renter, email: ApiFactory.UniqueEmail("sapu-hidup"));

        await api.SweepRefreshTokensAsync();

        var lanjut = await api.CreateClient().PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = akun.RefreshToken });

        lanjut.EnsureSuccessStatusCode();
    }
}
