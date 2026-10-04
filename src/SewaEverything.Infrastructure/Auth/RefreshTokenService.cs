using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Auth;

public sealed record IssuedRefreshToken(string Value, DateTime ExpiresAt);

public enum RefreshOutcome
{
    Rotated,

    NotFound,

    Expired,

    Revoked,

    Reused,

    AccountDeactivated
}

public sealed record RefreshRotation(
    RefreshOutcome Outcome,
    User? User = null,
    IssuedRefreshToken? Token = null);

public sealed class RefreshTokenService(
    SewaDbContext db, IOptions<JwtOptions> options, TimeProvider clock)
{
    private const int TokenByteLength = 32;

    private readonly JwtOptions _options = options.Value;

    public Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct) =>
        CreateAsync(userId, Guid.NewGuid(), ct);

    public async Task<RefreshRotation> RotateAsync(string presented, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return new RefreshRotation(RefreshOutcome.NotFound);
        }

        var hash = Hash(presented);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var token = await db.RefreshTokens.AsNoTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null)
        {
            return new RefreshRotation(RefreshOutcome.NotFound);
        }

        if (token.RevokedAt is not null)
        {
            return new RefreshRotation(RefreshOutcome.Revoked);
        }

        if (token.UsedAt is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevokeReasons.ReuseDetected, ct);
            await tx.CommitAsync(ct);
            return new RefreshRotation(RefreshOutcome.Reused);
        }

        if (token.ExpiresAt <= now)
        {
            return new RefreshRotation(RefreshOutcome.Expired);
        }

        var user = token.User!;

        if (!user.IsActive)
        {
            return new RefreshRotation(RefreshOutcome.AccountDeactivated);
        }

        var burned = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE refresh_tokens SET used_at = now()
            WHERE id = {token.Id} AND used_at IS NULL AND revoked_at IS NULL
            """, ct);

        if (burned == 0)
        {
            await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevokeReasons.ReuseDetected, ct);
            await tx.CommitAsync(ct);
            return new RefreshRotation(RefreshOutcome.Reused);
        }

        var issued = await CreateAsync(user.Id, token.FamilyId, ct);

        await tx.CommitAsync(ct);

        return new RefreshRotation(RefreshOutcome.Rotated, user, issued);
    }

    public async Task<bool> RevokeAsync(string presented, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var hash = Hash(presented);

        var familyId = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .FirstOrDefaultAsync(ct);

        if (familyId is null)
        {
            return false;
        }

        await RevokeFamilyAsync(familyId.Value, RefreshTokenRevokeReasons.Logout, ct);
        return true;
    }

    public Task<int> RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE refresh_tokens SET revoked_at = now(), revoked_reason = {reason}
            WHERE id IN (
                SELECT id FROM refresh_tokens
                WHERE user_id = {userId} AND revoked_at IS NULL
                ORDER BY id
                FOR UPDATE
            )
            """, ct);

    private async Task<IssuedRefreshToken> CreateAsync(Guid userId, Guid familyId, CancellationToken ct)
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));
        var expiresAt = clock.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = userId,
            FamilyId  = familyId,
            TokenHash = Hash(value),
            ExpiresAt = expiresAt
        });

        await db.SaveChangesAsync(ct);

        return new IssuedRefreshToken(value, expiresAt);
    }

    private Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE refresh_tokens SET revoked_at = now(), revoked_reason = {reason}
            WHERE id IN (
                SELECT id FROM refresh_tokens
                WHERE family_id = {familyId} AND revoked_at IS NULL
                ORDER BY id
                FOR UPDATE
            )
            """, ct);

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
