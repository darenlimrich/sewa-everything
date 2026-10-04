using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("items/{id:guid}/blackouts")]
[Authorize(Roles = Roles.Seller)]
public sealed class ItemBlackoutsController(SewaDbContext db) : ItemScopedController(db)
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ItemBlackoutResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ItemBlackoutResponse>>> List(
        Guid id, CancellationToken ct)
    {
        var (item, error) = await LoadOwnedItemAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var blackouts = await Db.ItemBlackouts
            .AsNoTracking()
            .Where(b => b.ItemId == item!.Id)
            .OrderBy(b => b.StartsAt)
            .ToListAsync(ct);

        return Ok(blackouts.Select(ApiMappings.ToResponse).ToList());
    }

    [HttpPost]
    [ProducesResponseType<ItemBlackoutResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ItemBlackoutResponse>> Create(
        Guid id, CreateBlackoutRequest request, CancellationToken ct)
    {
        if (request.EndsAt <= request.StartsAt)
        {
            ModelState.AddModelError(nameof(request.EndsAt),
                "Waktu selesai harus setelah waktu mulai.");
            return ValidationProblem(ModelState);
        }

        var (item, error) = await LoadOwnedItemAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var blackout = new ItemBlackout
        {
            ItemId = item!.Id,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        };

        Db.ItemBlackouts.Add(blackout);
        Db.SetDuring(blackout, request.StartsAt.UtcDateTime, request.EndsAt.UtcDateTime);

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            Db.ChangeTracker.Clear();

            var detail = pg.ConstraintName == "no_blackout_overlap"
                ? "Rentang ini bertabrakan dengan blackout lain yang sudah ada."
                : "Rentang ini sudah dipesan penyewa. Batalkan booking-nya lebih dulu, " +
                  "atau pilih rentang lain.";

            return Problem(
                title: "Rentang waktu bentrok",
                detail: detail,
                statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(nameof(List), new { id = item.Id }, blackout.ToResponse());
    }

    [HttpDelete("{blackoutId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, Guid blackoutId, CancellationToken ct)
    {
        var (item, error) = await LoadOwnedItemAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var blackout = await Db.ItemBlackouts
            .FirstOrDefaultAsync(b => b.Id == blackoutId && b.ItemId == item!.Id, ct);

        if (blackout is null)
        {
            return Problem(
                title: "Blackout tidak ditemukan",
                detail: $"Barang ini tidak punya blackout dengan id {blackoutId}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        Db.ItemBlackouts.Remove(blackout);
        await Db.SaveChangesAsync(ct);

        return NoContent();
    }
}
