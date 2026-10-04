using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("cart")]
[Authorize(Roles = Roles.Renter)]
public sealed class CartController(SewaDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    [HttpGet]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CartResponse>> List(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var rows = await Visible()
            .Where(c => c.RenterId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var itemIds = rows.Select(c => c.ItemId).ToList();

        var taken = await db.Bookings
            .Where(b => itemIds.Contains(b.ItemId) && BookingStatuses.NonTerminal.Contains(b.Status))
            .Select(b => new { b.ItemId, b.StartsAt, b.EndsAt })
            .ToListAsync(ct);

        var blackouts = await db.ItemBlackouts
            .Where(x => itemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.StartsAt, x.EndsAt })
            .ToListAsync(ct);

        var items = rows.Select(c => new CartItemResponse
        {
            Id            = c.Id,
            ItemId        = c.ItemId,
            ItemTitle     = c.Item?.Title ?? string.Empty,
            ItemPhotoUrl  = c.Item?.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).FirstOrDefault(),
            SellerId      = c.Item?.SellerId ?? Guid.Empty,
            SellerName    = c.Item?.Seller?.Name ?? string.Empty,
            Price         = c.Item?.Price ?? 0m,
            PriceUnit     = c.Item?.PriceUnit.ToDbValue() ?? PriceUnits.Day,
            DepositAmount = c.Item?.DepositAmount ?? 0m,
            DeliveryFee   = c.Item != null ? c.Item.DeliveryFee : null,
            StartAt       = c.StartAt,
            EndAt         = c.EndAt,
            Available     = c.Item?.IsPubliclyVisible == true
                            && c.Item.Seller?.IsVerified == true
                            && !taken.Any(b => b.ItemId == c.ItemId
                                            && b.StartsAt < c.EndAt && c.StartAt < b.EndsAt)
                            && !blackouts.Any(x => x.ItemId == c.ItemId
                                            && x.StartsAt < c.EndAt && c.StartAt < x.EndsAt),
            CreatedAt     = c.CreatedAt
        }).ToList();

        return Ok(new CartResponse { Items = items, Count = items.Count });
    }

    [HttpPost]
    [ProducesResponseType<CartItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartItemResponse>> Add(AddCartItemRequest request, CancellationToken ct)
    {
        var now    = clock.GetUtcNow().UtcDateTime;
        var userId = HttpContext.User.UserId();
        var from   = request.StartAt.ToUniversalTime();
        var to     = request.EndAt.ToUniversalTime();

        if (to <= from)
        {
            ModelState.AddModelError(nameof(request.EndAt),
                "Waktu selesai harus setelah waktu mulai.");
            return ValidationProblem(ModelState);
        }

        if (from < now - ClockSkew)
        {
            ModelState.AddModelError(nameof(request.StartAt),
                "Tidak bisa memesan waktu yang sudah lewat.");
            return ValidationProblem(ModelState);
        }

        var item = await db.Items
            .Include(i => i.Seller)
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, ct);

        if (item is null || !item.IsPubliclyVisible || item.Seller?.IsVerified != true)
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang yang bisa disewa dengan id {request.ItemId}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var existing = await db.CartItems
            .FirstOrDefaultAsync(c => c.RenterId == userId && c.ItemId == request.ItemId, ct);

        if (existing is null)
        {
            existing = new CartItem
            {
                RenterId = userId,
                ItemId   = request.ItemId,
                StartAt  = from,
                EndAt    = to
            };

            db.CartItems.Add(existing);
        }
        else
        {
            existing.StartAt = from;
            existing.EndAt   = to;
        }

        await db.SaveChangesAsync(ct);

        var saved = await Visible().FirstAsync(c => c.Id == existing.Id, ct);

        return Ok(new CartItemResponse
        {
            Id            = saved.Id,
            ItemId        = saved.ItemId,
            ItemTitle     = saved.Item?.Title ?? string.Empty,
            ItemPhotoUrl  = saved.Item?.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).FirstOrDefault(),
            SellerId      = saved.Item?.SellerId ?? Guid.Empty,
            SellerName    = saved.Item?.Seller?.Name ?? string.Empty,
            Price         = saved.Item?.Price ?? 0m,
            PriceUnit     = saved.Item?.PriceUnit.ToDbValue() ?? PriceUnits.Day,
            DepositAmount = saved.Item?.DepositAmount ?? 0m,
            DeliveryFee   = saved.Item?.DeliveryFee,
            StartAt       = saved.StartAt,
            EndAt         = saved.EndAt,
            Available     = true,
            CreatedAt     = saved.CreatedAt
        });
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var row = await db.CartItems
            .FirstOrDefaultAsync(c => c.Id == id && c.RenterId == userId, ct);

        if (row is null)
        {
            return Problem(
                title: "Baris keranjang tidak ditemukan",
                detail: $"Tidak ada baris keranjang milik Anda dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        db.CartItems.Remove(row);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private IQueryable<CartItem> Visible() => db.CartItems
        .Include(c => c.Item).ThenInclude(i => i!.Seller)
        .Include(c => c.Item).ThenInclude(i => i!.Photos);
}
