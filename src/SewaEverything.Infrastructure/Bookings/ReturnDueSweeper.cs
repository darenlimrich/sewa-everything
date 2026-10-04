using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Bookings;

public sealed class ReturnDueSweeper(
    SewaDbContext db,
    BookingCompletion completion,
    TimeProvider clock,
    ILogger<ReturnDueSweeper> logger)
{
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now        = clock.GetUtcNow().UtcDateTime;
        var windowDays = await db.PlatformSettings.Select(s => s.ReturnWindowDays).FirstAsync(ct);
        var cutoff     = now.AddDays(-windowDays);

        var dueIds = await db.Bookings
            .Where(b => b.Status == BookingStatus.Active && b.EndsAt < cutoff)
            .OrderBy(b => b.EndsAt)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var completed = 0;

        foreach (var id in dueIds)
        {
            try
            {
                var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);

                if (booking is null || booking.Status != BookingStatus.Active)
                {
                    continue;
                }

                await completion.CreateSettlementAsync(booking, deduction: 0m, ct);
                booking.Status = BookingStatus.Completed;

                await db.SaveChangesAsync(ct);
                completed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Gagal menyelesaikan booking {BookingId} secara otomatis; akan dicoba lagi.", id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        if (completed > 0)
        {
            logger.LogInformation(
                "Sewa yang lewat jendela pengembalian diselesaikan otomatis: {Completed} booking.",
                completed);
        }

        return completed;
    }
}
