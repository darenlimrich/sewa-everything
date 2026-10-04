using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;
using SewaEverything.Infrastructure.Security;

namespace SewaEverything.Infrastructure.Auth;

public enum TotpEnrollOutcome
{
    Started,

    AlreadyEnabled,

    ProtectionUnavailable
}

public sealed record TotpEnrollment(
    TotpEnrollOutcome Outcome, string? Secret = null, string? Uri = null);

public enum TotpConfirmOutcome
{
    Enabled,

    NotStarted,

    AlreadyEnabled,

    WrongCode
}

public sealed record TotpConfirmation(
    TotpConfirmOutcome Outcome, IReadOnlyList<string> RecoveryCodes);

public enum TotpLoginCheck
{
    NotRequired,

    Ok,

    Missing,

    Wrong
}

public sealed record TotpStatus(
    bool Enabled, bool PendingSetup, DateTime? EnabledAt, int RecoveryCodesLeft);

public sealed class TotpService(
    SewaDbContext db,
    ISecretProtector protector,
    IOptions<TotpOptions> options,
    TimeProvider clock,
    ILogger<TotpService> logger)
{
    private const int RecoveryCodeChars = 10;

    private readonly TotpOptions _options = options.Value;

    public async Task<TotpStatus> StatusAsync(Guid userId, CancellationToken ct)
    {
        var totp = await db.UserTotps
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (totp is null)
        {
            return new TotpStatus(false, false, null, 0);
        }

        if (!totp.IsConfirmed)
        {
            return new TotpStatus(false, true, null, 0);
        }

        var tersisa = await db.TotpRecoveryCodes
            .CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);

        return new TotpStatus(true, false, totp.ConfirmedAt, tersisa);
    }

    public async Task<TotpEnrollment> BeginAsync(User user, CancellationToken ct)
    {
        if (!protector.IsConfigured)
        {
            return new TotpEnrollment(TotpEnrollOutcome.ProtectionUnavailable);
        }

        var tersimpan = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == user.Id, ct);

        if (tersimpan is { IsConfirmed: true })
        {
            return new TotpEnrollment(TotpEnrollOutcome.AlreadyEnabled);
        }

        if (tersimpan is not null)
        {
            db.UserTotps.Remove(tersimpan);
            await db.SaveChangesAsync(ct);
        }

        var secret = Totp.NewSecret();
        var base32 = Base32.Encode(secret);

        db.UserTotps.Add(new UserTotp
        {
            UserId = user.Id,
            Secret = protector.Protect(base32)
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Pendaftaran faktor kedua dimulai untuk akun {UserId}.", user.Id);

        return new TotpEnrollment(
            TotpEnrollOutcome.Started,
            base32,
            Totp.BuildUri(_options.Issuer, user.Email, base32));
    }

    public async Task<TotpConfirmation> ConfirmAsync(Guid userId, string? code, CancellationToken ct)
    {
        var totp = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (totp is null)
        {
            return new TotpConfirmation(TotpConfirmOutcome.NotStarted, []);
        }

        if (totp.IsConfirmed)
        {
            return new TotpConfirmation(TotpConfirmOutcome.AlreadyEnabled, []);
        }

        var now = clock.GetUtcNow();

        if (!Totp.TryMatch(Secret(totp), code, Totp.StepAt(now), _options.WindowSteps, out var step))
        {
            return new TotpConfirmation(TotpConfirmOutcome.WrongCode, []);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        totp.Confirm(now.UtcDateTime, step);

        var kode = new List<string>(_options.RecoveryCodeCount);

        for (var i = 0; i < _options.RecoveryCodeCount; i++)
        {
            var satu = NewRecoveryCode();
            kode.Add(satu);

            db.TotpRecoveryCodes.Add(new TotpRecoveryCode
            {
                UserId   = userId,
                CodeHash = HashCode(satu)
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogWarning("Faktor kedua dinyalakan untuk akun {UserId}.", userId);

        return new TotpConfirmation(TotpConfirmOutcome.Enabled, kode);
    }

    public async Task<bool> DisableAsync(Guid userId, CancellationToken ct)
    {
        var totp = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (totp is null)
        {
            return false;
        }

        db.UserTotps.Remove(totp);
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Faktor kedua dimatikan untuk akun {UserId}.", userId);

        return true;
    }

    public async Task<TotpLoginCheck> VerifyForLoginAsync(
        Guid userId, string? code, CancellationToken ct)
    {
        var totp = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (totp is null || !totp.IsConfirmed)
        {
            return TotpLoginCheck.NotRequired;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return TotpLoginCheck.Missing;
        }

        var now = clock.GetUtcNow();

        if (Totp.TryMatch(Secret(totp), code, Totp.StepAt(now), _options.WindowSteps, out var step))
        {
            if (!totp.Accepts(step))
            {
                logger.LogWarning(
                    "Kode faktor kedua akun {UserId} ditolak: langkah {Step} sudah pernah dipakai.",
                    userId, step);

                return TotpLoginCheck.Wrong;
            }

            totp.RecordStep(step);
            await db.SaveChangesAsync(ct);

            return TotpLoginCheck.Ok;
        }

        if (await TryBurnRecoveryCodeAsync(userId, code, ct))
        {
            logger.LogWarning(
                "Akun {UserId} masuk memakai kode pemulihan, bukan kode faktor kedua.", userId);

            return TotpLoginCheck.Ok;
        }

        return TotpLoginCheck.Wrong;
    }

    private async Task<bool> TryBurnRecoveryCodeAsync(
        Guid userId, string code, CancellationToken ct)
    {
        var hash = HashCode(code);

        var terpakai = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE totp_recovery_codes SET used_at = now()
            WHERE user_id = {userId} AND code_hash = {hash} AND used_at IS NULL
            """, ct);

        return terpakai == 1;
    }

    private byte[] Secret(UserTotp totp)
    {
        if (!Base32.TryDecode(protector.Reveal(totp.Secret), out var bytes))
        {
            throw new InvalidOperationException(
                "Rahasia faktor kedua tersimpan dalam bentuk yang tidak dapat dibaca. " +
                "Periksa Security:SecretKey, atau daftarkan ulang faktor keduanya.");
        }

        return bytes;
    }

    private static string NewRecoveryCode()
    {
        var mentah = Base32.Encode(RandomNumberGenerator.GetBytes(8))[..RecoveryCodeChars];

        return $"{mentah[..5]}-{mentah[5..]}";
    }

    private static byte[] HashCode(string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));

    private static string Normalize(string code) =>
        new([.. code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
