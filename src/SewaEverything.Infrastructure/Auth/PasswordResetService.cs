using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Email;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Auth;

public enum PasswordResetOutcome
{
    Reset,

    InvalidToken,

    WeakPassword,

    AccountDeactivated
}

public sealed record PasswordResetResult(PasswordResetOutcome Outcome, string? Message = null);

public sealed class PasswordResetService(
    SewaDbContext db,
    IPasswordHasher hasher,
    IEmailSender email,
    RefreshTokenService refreshTokens,
    IOptions<PasswordResetOptions> options,
    TimeProvider clock,
    ILogger<PasswordResetService> logger)
{
    private const int TokenByteLength = 32;

    private readonly PasswordResetOptions _options = options.Value;

    public async Task RequestAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = emailAddress.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);

        if (user is null)
        {
            logger.LogInformation(
                "Permintaan atur ulang kata sandi untuk email yang tidak terdaftar. " +
                "Tidak ada surel yang dikirim.");
            return;
        }

        if (!user.IsActive)
        {
            logger.LogWarning(
                "Permintaan atur ulang kata sandi untuk akun {UserId} yang aksesnya sudah dicabut. " +
                "Tidak ada surel yang dikirim.", user.Id);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;

        var lastRequested = await db.PasswordResets
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => (DateTime?)r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastRequested is { } terakhir && now - terakhir < _options.MinInterval)
        {
            logger.LogInformation(
                "Permintaan atur ulang kata sandi untuk akun {UserId} ditahan karena masih dalam " +
                "jeda antar permintaan.", user.Id);
            return;
        }

        var batasSimpan = now - TimeSpan.FromDays(_options.RetentionDays);

        await db.PasswordResets
            .Where(r => r.UserId == user.Id && r.ExpiresAt < batasSimpan)
            .ExecuteDeleteAsync(ct);

        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));

        db.PasswordResets.Add(new PasswordReset
        {
            UserId    = user.Id,
            TokenHash = Hash(value),
            ExpiresAt = now + _options.Lifetime
        });

        await db.SaveChangesAsync(ct);

        try
        {
            await email.SendAsync(Compose(user, BuildLink(value)), ct);

            logger.LogInformation(
                "Tautan atur ulang kata sandi dikirim untuk akun {UserId}.", user.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Tautan atur ulang kata sandi untuk akun {UserId} gagal dikirim. Pengguna tidak " +
                "diberi tahu — pemberitahuannya akan membedakan email terdaftar dari yang tidak.",
                user.Id);
        }
    }

    public async Task<PasswordResetResult> ResetAsync(
        string token,
        string newPassword,
        Func<string, string?, string?, string?>? periksaSandi,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PasswordResetResult(PasswordResetOutcome.InvalidToken);
        }

        var hash = Hash(token);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var reset = await db.PasswordResets
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (reset is null || !reset.IsLive(now))
        {
            return new PasswordResetResult(PasswordResetOutcome.InvalidToken);
        }

        var user = reset.User!;

        if (!user.IsActive)
        {
            return new PasswordResetResult(PasswordResetOutcome.AccountDeactivated);
        }

        if (periksaSandi?.Invoke(newPassword, user.Email, user.Name) is { } lemah)
        {
            return new PasswordResetResult(PasswordResetOutcome.WeakPassword, lemah);
        }

        var burned = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE password_resets SET used_at = now()
            WHERE user_id = {user.Id} AND used_at IS NULL
            """, ct);

        if (burned == 0)
        {
            return new PasswordResetResult(PasswordResetOutcome.InvalidToken);
        }

        user.PasswordHash = hasher.Hash(newPassword);
        user.RegisterSuccessfulLogin();

        await db.SaveChangesAsync(ct);

        await refreshTokens.RevokeAllForUserAsync(
            user.Id, RefreshTokenRevokeReasons.PasswordReset, ct);

        await tx.CommitAsync(ct);

        logger.LogWarning(
            "Kata sandi akun {UserId} diatur ulang lewat tautan surel. Seluruh sesi lamanya dicabut.",
            user.Id);

        return new PasswordResetResult(PasswordResetOutcome.Reset);
    }

    private string BuildLink(string token) =>
        $"{_options.WebBaseUrl.TrimEnd('/')}{_options.LinkPath}?token={Uri.EscapeDataString(token)}";

    private EmailMessage Compose(User user, string link)
    {
        var menit = _options.LifetimeMinutes;

        var teks =
            $"""
             Halo {user.Name},

             Kami menerima permintaan untuk mengatur ulang kata sandi akun Sewaku Anda
             ({user.Email}). Buka tautan berikut untuk memilih kata sandi baru:

             {link}

             Tautan ini berlaku {menit} menit dan hanya dapat dipakai satu kali.

             Jika Anda tidak meminta ini, abaikan surel ini. Kata sandi Anda tidak berubah
             selama tautan di atas tidak dibuka, dan tidak ada seorang pun di tim Sewaku
             yang dapat mengubahnya untuk Anda.

             Sewaku
             """;

        var nama = WebUtility.HtmlEncode(user.Name);
        var alamat = WebUtility.HtmlEncode(user.Email);
        var tautan = WebUtility.HtmlEncode(link);

        var html =
            $"""
             <div style="font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:15px;line-height:1.6;color:#1D1D1F">
               <p>Halo {nama},</p>
               <p>Kami menerima permintaan untuk mengatur ulang kata sandi akun Sewaku Anda ({alamat}).</p>
               <p style="margin:24px 0">
                 <a href="{tautan}" style="background:#EE4D5F;color:#fff;text-decoration:none;padding:12px 20px;border-radius:8px;display:inline-block;font-weight:600">Atur kata sandi baru</a>
               </p>
               <p style="color:#6E6E73;font-size:13px">Jika tombol di atas tidak bekerja, salin alamat ini ke peramban Anda:<br><span style="word-break:break-all">{tautan}</span></p>
               <p>Tautan ini berlaku <strong>{menit} menit</strong> dan hanya dapat dipakai satu kali.</p>
               <p style="color:#6E6E73;font-size:13px">Jika Anda tidak meminta ini, abaikan surel ini. Kata sandi Anda tidak berubah selama tautan di atas tidak dibuka, dan tidak ada seorang pun di tim Sewaku yang dapat mengubahnya untuk Anda.</p>
               <p style="color:#6E6E73;font-size:13px">Sewaku</p>
             </div>
             """;

        return new EmailMessage(
            user.Email, user.Name, "Atur ulang kata sandi Sewaku", teks, html);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
