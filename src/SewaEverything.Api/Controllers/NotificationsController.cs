using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public sealed class NotificationsController(SewaDbContext db, TimeProvider clock) : ControllerBase
{
    private const int MaxItems = 30;

    [HttpGet]
    [ProducesResponseType<NotificationListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationListResponse>> List(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var seen = user.NotificationsSeenAt;

        var bookings = await db.Bookings
            .Include(b => b.Item).ThenInclude(i => i!.Seller)
            .Include(b => b.Item).ThenInclude(i => i!.Photos)
            .Include(b => b.Renter)
            .Where(b => b.RenterId == userId || b.Item!.SellerId == userId)
            .OrderByDescending(b => b.UpdatedAt)
            .Take(MaxItems)
            .ToListAsync(ct);

        var listings = user.Role == UserRole.Seller
            ? await db.Items
                .AsNoTracking()
                .Include(i => i.Photos)
                .Where(i => i.SellerId == userId && i.ReviewedBy != null)
                .OrderByDescending(i => i.ReviewedAt)
                .Take(MaxItems)
                .ToListAsync(ct)
            : [];

        var items = bookings
            .Select(b => Build(b, userId, seen))
            .Concat(listings.Select(i => Build(i, seen)))
            .OrderByDescending(n => n.At)
            .Take(MaxItems)
            .ToList();

        return Ok(new NotificationListResponse
        {
            Items       = items,
            UnreadCount = items.Count(n => n.Unread),
            SeenAt      = seen
        });
    }

    [HttpPost("seen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkSeen(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        user.MarkNotificationsSeen(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static NotificationResponse Build(Item i, DateTime? seen) => new()
    {
        ItemId          = i.Id,
        Kind            = NotificationKinds.Listing,
        Status          = i.ReviewStatus.ToDbValue(),
        ItemTitle       = i.Title,
        ItemPhotoUrl    = i.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).FirstOrDefault(),
        CounterpartName = "Admin",
        At              = i.ReviewedAt!.Value,
        Unread          = seen is null || i.ReviewedAt > seen
    };

    private static NotificationResponse Build(Booking b, Guid userId, DateTime? seen)
    {
        var asRenter = b.RenterId == userId;

        return new NotificationResponse
        {
            BookingId       = b.Id,
            Reference       = b.Reference,
            Kind            = asRenter ? NotificationKinds.Renter : NotificationKinds.Seller,
            Status          = b.Status.ToDbValue(),
            ItemTitle       = b.Item?.Title ?? string.Empty,
            ItemPhotoUrl    = b.Item?.Photos
                                  .OrderBy(p => p.SortOrder)
                                  .Select(p => p.Url)
                                  .FirstOrDefault(),
            CounterpartName = asRenter
                ? b.Item?.Seller?.Name ?? string.Empty
                : b.Renter?.Name ?? string.Empty,
            Amount          = asRenter ? b.RenterTotal : b.SellerGross,
            HoldExpiresAt   = b.HoldExpiresAt,
            At              = b.UpdatedAt,
            Unread          = seen is null || b.UpdatedAt > seen
        };
    }
}
