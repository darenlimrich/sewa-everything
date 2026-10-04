using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("addresses")]
[Authorize]
public sealed class AddressesController(SewaDbContext db) : ControllerBase
{
    private const int MaxPerUser = 20;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AddressResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AddressResponse>>> List(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var rows = await db.Addresses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return Ok(rows.Select(a => a.ToResponse()).ToList());
    }

    [HttpPost]
    [ProducesResponseType<AddressResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AddressResponse>> Create(SaveAddressRequest request, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var jumlah = await db.Addresses.CountAsync(a => a.UserId == userId, ct);
        if (jumlah >= MaxPerUser)
        {
            return Problem(
                title: "Alamat terlalu banyak",
                detail: $"Maksimal {MaxPerUser} alamat tersimpan. Hapus salah satu lebih dahulu.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var pertama = jumlah == 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (request.IsDefault || pertama)
        {
            await ClearDefaultAsync(userId, ct);
        }

        var address = new Address
        {
            UserId        = userId,
            Label         = request.Label.Trim(),
            RecipientName = request.RecipientName.Trim(),
            Phone         = request.Phone.Trim(),
            FullAddress   = request.FullAddress.Trim(),
            Notes         = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsDefault     = request.IsDefault || pertama
        };

        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CreatedAtAction(nameof(List), new { id = address.Id }, address.ToResponse());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AddressResponse>> Update(Guid id, SaveAddressRequest request, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (address is null)
        {
            return NotFound();
        }

        if (request.IsDefault && !address.IsDefault)
        {
            await ClearDefaultAsync(userId, ct);
        }

        address.Label         = request.Label.Trim();
        address.RecipientName = request.RecipientName.Trim();
        address.Phone         = request.Phone.Trim();
        address.FullAddress   = request.FullAddress.Trim();
        address.Notes         = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        if (request.IsDefault)
        {
            address.IsDefault = true;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(address.ToResponse());
    }

    [HttpPost("{id:guid}/default")]
    [ProducesResponseType<AddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AddressResponse>> MakeDefault(Guid id, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (address is null)
        {
            return NotFound();
        }

        await ClearDefaultAsync(userId, ct);
        address.IsDefault = true;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(address.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (address is null)
        {
            return NotFound();
        }

        var wasDefault = address.IsDefault;

        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);

        if (wasDefault)
        {
            var pengganti = await db.Addresses
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (pengganti is not null)
            {
                pengganti.IsDefault = true;
                await db.SaveChangesAsync(ct);
            }
        }

        await tx.CommitAsync(ct);

        return NoContent();
    }

    private async Task ClearDefaultAsync(Guid userId, CancellationToken ct) =>
        await db.Addresses
            .Where(a => a.UserId == userId && a.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
}
