using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("payout-accounts")]
[Authorize]
public sealed class PayoutAccountsController(SewaDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PayoutAccountResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PayoutAccountResponse>>> List(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var accounts = await db.PayoutAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

        return Ok(accounts.Select(ApiMappings.ToResponse).ToList());
    }

    [HttpPost]
    [ProducesResponseType<PayoutAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayoutAccountResponse>> Create(
        CreatePayoutAccountRequest request, CancellationToken ct)
    {
        if (!PayoutAccountKinds.IsKnown(request.Kind))
        {
            ModelState.AddModelError(nameof(request.Kind),
                $"Jenis rekening hanya boleh '{PayoutAccountKinds.Bank}' atau " +
                $"'{PayoutAccountKinds.Ewallet}'.");
            return ValidationProblem(ModelState);
        }

        var userId = HttpContext.User.UserId();

        var isFirst = !await db.PayoutAccounts.AnyAsync(a => a.UserId == userId, ct);

        var account = new PayoutAccount
        {
            UserId        = userId,
            Kind          = PayoutAccountKinds.FromDbValue(request.Kind),
            ProviderCode  = request.ProviderCode.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            AccountHolder = request.AccountHolder.Trim(),

            IsDefault = isFirst || request.IsDefault
        };

        if (account.IsDefault)
        {
            await ClearDefaultAsync(userId, ct);
        }

        db.PayoutAccounts.Add(account);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Rekening sudah terdaftar",
                detail: "Rekening dengan kode dan nomor yang sama sudah ada di akun Anda.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(nameof(List), new { }, account.ToResponse());
    }

    [HttpPost("{id:guid}/default")]
    [ProducesResponseType<PayoutAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayoutAccountResponse>> MakeDefault(Guid id, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var account = await db.PayoutAccounts
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

        if (account is null)
        {
            return NotFoundProblem(id);
        }

        await ClearDefaultAsync(userId, ct);

        account.IsDefault = true;
        await db.SaveChangesAsync(ct);

        return Ok(account.ToResponse());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM payout_accounts WHERE user_id = {userId} ORDER BY id FOR UPDATE
            """, ct);

        var account = await db.PayoutAccounts
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);

        if (account is null)
        {
            return NotFoundProblem(id);
        }

        var terpakai = await db.Payments.AnyAsync(
            p => p.PayoutAccountId == id && p.Status == PaymentStatus.Pending, ct);

        if (terpakai)
        {
            return Problem(
                title: "Rekening masih dipakai",
                detail: "Masih ada pengembalian dana atau pencairan yang menunggu dikirim ke " +
                        "rekening ini. Selesaikan dulu sebelum menghapusnya.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var terakhir = !await db.PayoutAccounts
            .AnyAsync(a => a.UserId == userId && a.Id != id, ct);

        if (terakhir && await MasihAkanDibayarAsync(userId, ct))
        {
            return Problem(
                title: "Rekening terakhir masih dibutuhkan",
                detail: "Masih ada sewa berjalan yang akan membayar kepada Anda, dan ini " +
                        "satu-satunya rekening tujuannya. Daftarkan rekening pengganti lebih " +
                        "dulu, lalu hapus yang ini.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.PayoutAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return NoContent();
    }

    private Task<bool> MasihAkanDibayarAsync(Guid userId, CancellationToken ct)
    {
        var hidup = BookingStatuses.NonTerminal;

        return db.Bookings.AnyAsync(
            b => hidup.Contains(b.Status)
                 && (b.Item!.SellerId == userId
                     || (b.RenterId == userId
                         && db.Payments.Any(p => p.BookingId == b.Id
                                                 && p.Direction == PaymentDirection.In
                                                 && p.Status == PaymentStatus.Paid
                                                 && p.Channel != null
                                                 && !PaymentChannels.ReversibleDbValues
                                                     .Contains(p.Channel)))),
            ct);
    }

    private async Task ClearDefaultAsync(Guid userId, CancellationToken ct)
    {
        var current = await db.PayoutAccounts
            .Where(a => a.UserId == userId && a.IsDefault)
            .ToListAsync(ct);

        foreach (var account in current)
        {
            account.IsDefault = false;
        }

        if (current.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private ObjectResult NotFoundProblem(Guid id) => Problem(
        title: "Rekening tidak ditemukan",
        detail: $"Tidak ada rekening dengan id {id} di akun Anda.",
        statusCode: StatusCodes.Status404NotFound);
}
