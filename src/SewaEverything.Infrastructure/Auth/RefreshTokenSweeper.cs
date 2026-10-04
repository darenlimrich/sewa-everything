using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Auth;

public sealed class RefreshTokenSweeper(
    SewaDbContext db,
    TimeProvider clock,
    IOptions<RefreshTokenSweeperOptions> options,
    ILogger<RefreshTokenSweeper> logger)
{
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);

        var removed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM refresh_tokens WHERE expires_at < {cutoff}
            """, ct);

        if (removed > 0)
        {
            logger.LogInformation(
                "Baris refresh token mati dibuang: {Removed} baris kedaluwarsa sebelum {Cutoff:o}.",
                removed, cutoff);
        }

        return removed;
    }
}
