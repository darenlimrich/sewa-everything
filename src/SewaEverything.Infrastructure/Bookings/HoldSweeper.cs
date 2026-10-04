using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Bookings;

public sealed class HoldSweeper(
    SewaDbContext db, TimeProvider clock, ILogger<HoldSweeper> logger)
{
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var released = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE bookings
            SET status = 'cancelled',
                cancelled_reason = CASE status
                    WHEN 'pending'
                        THEN 'Hold kedaluwarsa: seller tidak menyetujui sampai batas waktu.'
                    ELSE 'Hold kedaluwarsa: pembayaran tidak diterima sampai batas waktu.'
                END,
                hold_expires_at = NULL
            WHERE status IN ('pending', 'confirmed')
              AND hold_expires_at IS NOT NULL
              AND hold_expires_at < {now}
            """, ct);

        if (released > 0)
        {
            logger.LogInformation("Hold kedaluwarsa dilepas: {Released} booking dibatalkan.", released);
        }

        return released;
    }
}
