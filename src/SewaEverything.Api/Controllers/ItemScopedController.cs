using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

public abstract class ItemScopedController(SewaDbContext db) : ControllerBase
{
    protected SewaDbContext Db { get; } = db;

    protected async Task<(Item? Item, ActionResult? Error)> LoadOwnedItemAsync(
        Guid itemId, CancellationToken ct)
    {
        var item = await Db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);

        if (item is null)
        {
            return (null, Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {itemId}.",
                statusCode: StatusCodes.Status404NotFound));
        }

        if (item.SellerId != HttpContext.User.UserId())
        {
            return (null, Problem(
                title: "Bukan barang Anda",
                detail: "Hanya pemilik listing yang boleh mengubahnya.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (item, null);
    }

    protected bool CanView(Item item)
    {
        if (item.IsPubliclyVisible && item.Seller?.IsVerified == true)
        {
            return true;
        }

        if (HttpContext.User.UserIdOrNull() == item.SellerId)
        {
            return true;
        }

        return HttpContext.User.RoleOrNull() is UserRole.Admin or UserRole.Owner;
    }
}
