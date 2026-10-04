using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("pulse")]
[Authorize]
public sealed class PulseController(SewaDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PulseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PulseResponse>> Get(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var role   = HttpContext.User.Role();

        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.NotificationsSeenAt })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var mine = db.Bookings.Where(b => b.RenterId == userId || b.Item!.SellerId == userId);

        var stamp = await mine
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => (DateTime?)b.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var seen = user.NotificationsSeenAt;

        var unread = seen is null
            ? await mine.CountAsync(ct)
            : await mine.CountAsync(b => b.UpdatedAt > seen, ct);

        if (role == UserRole.Seller)
        {
            var decided = db.Items.Where(i => i.SellerId == userId && i.ReviewedBy != null);

            unread += seen is null
                ? await decided.CountAsync(ct)
                : await decided.CountAsync(i => i.ReviewedAt > seen, ct);
        }

        var action = role switch
        {
            UserRole.Seller => await mine.CountAsync(
                b => b.Item!.SellerId == userId
                     && (b.Status == BookingStatus.Pending
                         || b.Status == BookingStatus.Active
                         || (b.Status == BookingStatus.Confirmed && b.HoldExpiresAt == null)), ct),
            UserRole.Renter => await mine.CountAsync(
                b => b.RenterId == userId
                     && b.Status == BookingStatus.Confirmed
                     && b.HoldExpiresAt != null, ct),
            _ => 0
        };

        var cart = role == UserRole.Renter
            ? await db.CartItems.CountAsync(c => c.RenterId == userId, ct)
            : 0;

        var staff = role == UserRole.Admin;

        var itemsStamp = role == UserRole.Seller
            ? await db.Items
                .Where(i => i.SellerId == userId)
                .OrderByDescending(i => i.UpdatedAt)
                .Select(i => (DateTime?)i.UpdatedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        var ledgerStamp = role == UserRole.Owner
            ? await db.Payments
                .OrderByDescending(p => p.UpdatedAt)
                .Select(p => (DateTime?)p.UpdatedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        return Ok(new PulseResponse
        {
            BookingsStamp  = stamp,
            ItemsStamp     = itemsStamp,
            LedgerStamp    = ledgerStamp,
            Unread         = unread,
            ActionNeeded   = action,
            CartCount      = cart,
            OpenDisputes   = staff
                ? await db.Disputes.CountAsync(d => d.Status == DisputeStatus.Open, ct)
                : 0,
            PendingPayouts = staff
                ? await db.Payments.CountAsync(
                    p => p.Direction == PaymentDirection.Out && p.Status == PaymentStatus.Pending, ct)
                : 0,
            PendingSellers = staff
                ? await db.Users.CountAsync(u => u.Role == UserRole.Seller && !u.IsVerified, ct)
                : 0,
            SuspendedItems = staff
                ? await db.Items.CountAsync(i => i.SuspendedAt != null, ct)
                : 0,
            PendingItems = staff
                ? await db.Items.CountAsync(i => i.ReviewStatus == ItemReviewStatus.Pending, ct)
                : 0
        });
    }
}
