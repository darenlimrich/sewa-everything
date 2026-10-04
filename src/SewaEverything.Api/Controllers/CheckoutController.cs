using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("checkout")]
[Authorize(Roles = Roles.Renter)]
public sealed class CheckoutController(SewaDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    [HttpPost("quote")]
    [ProducesResponseType<CheckoutQuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CheckoutQuoteResponse>> Quote(CheckoutRequest request, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        var siap = await SiapkanAsync(userId, request, ct);
        if (siap.Error is { } error)
        {
            return error;
        }

        return Ok(siap.Quote!);
    }

    [HttpPost]
    [ProducesResponseType<CheckoutResultResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckoutResultResponse>> Submit(CheckoutRequest request, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var now    = clock.GetUtcNow().UtcDateTime;

        var siap = await SiapkanAsync(userId, request, ct);
        if (siap.Error is { } error)
        {
            return error;
        }

        var quote = siap.Quote!;

        if (!quote.CanSubmit)
        {
            var bermasalah = quote.Lines.FirstOrDefault(l => !l.Available);

            return Problem(
                title: "Sewa tidak dapat diajukan",
                detail: bermasalah?.Problem
                        ?? "Sebagian barang yang dipilih tidak dapat disewa untuk tanggal itu.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var settings = await db.PlatformSettings.AsNoTracking().FirstAsync(ct);
        var alamat   = siap.Address;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var lahir = new List<Booking>();

        foreach (var baris in siap.Baris)
        {
            var metode = baris.Metode;

            var booking = new Booking
            {
                ItemId            = baris.Item.Id,
                RenterId          = userId,
                Status            = BookingStatus.Pending,
                PriceSnapshot     = baris.Quote.PriceSnapshot,
                PriceUnitSnapshot = baris.Quote.PriceUnitSnapshot,
                DurationUnits     = baris.Quote.DurationUnits,
                TotalRent         = baris.Quote.TotalRent,
                DepositAmount     = baris.Quote.DepositAmount,
                PlatformFeeRate   = baris.Quote.PlatformFeeRate,
                PlatformFeeMode   = baris.Quote.PlatformFeeMode,
                PlatformFeeAmount = baris.Quote.PlatformFeeAmount,

                DeliveryMethod    = metode,
                DeliveryFee       = baris.Quote.DeliveryFee,
                DeliveryRecipient = metode == DeliveryMethod.Delivery ? alamat!.RecipientName : null,
                DeliveryPhone     = metode == DeliveryMethod.Delivery ? alamat!.Phone : null,
                DeliveryAddress   = metode == DeliveryMethod.Delivery ? alamat!.FullAddress : null,
                DeliveryNotes     = metode == DeliveryMethod.Delivery ? alamat!.Notes : null,

                HoldExpiresAt = now.AddMinutes(settings.ApprovalMinutes)
            };

            db.Bookings.Add(booking);
            db.SetDuring(booking, baris.From, baris.To);
            lahir.Add(booking);
        }

        var dipakai = siap.Baris.Select(b => b.CartItemId).ToList();

        db.CartItems.RemoveRange(
            await db.CartItems.Where(c => c.RenterId == userId && dipakai.Contains(c.Id)).ToListAsync(ct));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            db.ChangeTracker.Clear();
            await tx.RollbackAsync(ct);

            return Problem(
                title: "Tanggal sudah terpakai",
                detail: pg.ConstraintName == "no_overlap"
                    ? "Salah satu tanggal baru saja diambil penyewa lain. Tidak ada sewa yang diajukan; " +
                      "periksa lagi keranjang Anda."
                    : "Salah satu tanggal diblokir pemilik barang. Tidak ada sewa yang diajukan.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await tx.CommitAsync(ct);

        var ids = lahir.Select(b => b.Id).ToList();

        var hasil = await db.Bookings
            .Include(b => b.Item)!.ThenInclude(i => i!.Photos)
            .Include(b => b.Item)!.ThenInclude(i => i!.Seller)
            .Include(b => b.Renter)
            .Where(b => ids.Contains(b.Id))
            .ToListAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new CheckoutResultResponse
        {
            Bookings = hasil.Select(b => b.ToResponse()).ToList(),
            Count    = hasil.Count
        });
    }

    private sealed record BarisSiap(
        Guid CartItemId, Item Item, DateTime From, DateTime To,
        DeliveryMethod Metode, BookingQuote Quote);

    private sealed record Persiapan(
        ActionResult? Error, CheckoutQuoteResponse? Quote, Address? Address, List<BarisSiap> Baris);

    private async Task<Persiapan> SiapkanAsync(Guid userId, CheckoutRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var diminta = request.Lines.ToList();

        if (diminta.Count == 0)
        {
            return new Persiapan(
                Problem(title: "Tidak ada barang", detail: "Pilih minimal satu barang di keranjang.",
                        statusCode: StatusCodes.Status400BadRequest),
                null, null, []);
        }

        if (diminta.Select(l => l.CartItemId).Distinct().Count() != diminta.Count)
        {
            return new Persiapan(
                Problem(title: "Barang kembar", detail: "Satu barang hanya boleh muncul sekali dalam satu pengajuan.",
                        statusCode: StatusCodes.Status400BadRequest),
                null, null, []);
        }

        foreach (var l in diminta)
        {
            if (!DeliveryMethods.IsKnown(l.DeliveryMethod))
            {
                return new Persiapan(
                    Problem(title: "Cara pengiriman tidak dikenal",
                            detail: $"Cara pengiriman \"{l.DeliveryMethod}\" tidak dikenal.",
                            statusCode: StatusCodes.Status400BadRequest),
                    null, null, []);
            }
        }

        var ids = diminta.Select(l => l.CartItemId).ToList();

        var rows = await db.CartItems
            .Include(c => c.Item)!.ThenInclude(i => i!.Photos)
            .Include(c => c.Item)!.ThenInclude(i => i!.Seller)
            .Where(c => c.RenterId == userId && ids.Contains(c.Id))
            .ToListAsync(ct);

        if (rows.Count != diminta.Count)
        {
            return new Persiapan(
                Problem(title: "Baris keranjang tidak ditemukan",
                        detail: "Sebagian barang sudah tidak ada di keranjang Anda. Muat ulang halaman.",
                        statusCode: StatusCodes.Status400BadRequest),
                null, null, []);
        }

        var perluAlamat = diminta.Any(l => l.DeliveryMethod == DeliveryMethodValues.Delivery);

        Address? alamat = null;

        if (perluAlamat)
        {
            alamat = request.AddressId is { } aid
                ? await db.Addresses.FirstOrDefaultAsync(a => a.Id == aid && a.UserId == userId, ct)
                : await db.Addresses
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync(ct);

            if (alamat is null)
            {
                return new Persiapan(
                    Problem(title: "Alamat belum ada",
                            detail: "Tambahkan alamat pengiriman terlebih dahulu.",
                            statusCode: StatusCodes.Status400BadRequest),
                    null, null, []);
            }
        }

        var settings = await db.PlatformSettings.AsNoTracking().FirstAsync(ct);

        var itemIds = rows.Select(c => c.ItemId).ToList();

        var terpakai = await db.Bookings
            .Where(b => itemIds.Contains(b.ItemId) && BookingStatuses.NonTerminal.Contains(b.Status))
            .Select(b => new { b.ItemId, b.StartsAt, b.EndsAt })
            .ToListAsync(ct);

        var blackouts = await db.ItemBlackouts
            .Where(x => itemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.StartsAt, x.EndsAt })
            .ToListAsync(ct);

        var siap  = new List<BarisSiap>();
        var lines = new List<CheckoutQuoteLineResponse>();

        foreach (var permintaan in diminta)
        {
            var c    = rows.First(r => r.Id == permintaan.CartItemId);
            var item = c.Item;

            var metodeDb = permintaan.DeliveryMethod;
            var metode   = DeliveryMethods.FromDbValue(metodeDb);

            var melayaniAntar = item?.DeliveryFee is not null;

            string? masalah = null;

            if (item is null || !item.IsPubliclyVisible || item.Seller?.IsVerified != true)
            {
                masalah = "Barang ini sudah tidak tersedia.";
            }
            else if (c.StartAt < now - ClockSkew)
            {
                masalah = "Tanggal mulainya sudah lewat. Pilih tanggal lain.";
            }
            else if (metode == DeliveryMethod.Delivery && !melayaniAntar)
            {
                masalah = "Pemilik barang ini tidak melayani pengantaran.";
            }
            else if (terpakai.Any(b => b.ItemId == c.ItemId && b.StartsAt < c.EndAt && c.StartAt < b.EndsAt)
                  || blackouts.Any(b => b.ItemId == c.ItemId && b.StartsAt < c.EndAt && c.StartAt < b.EndsAt))
            {
                masalah = "Tanggalnya sudah diambil lebih dulu. Pilih tanggal lain.";
            }

            var quote = masalah is null && item is not null
                ? BookingCalculator.Quote(item, settings, c.StartAt, c.EndAt, metode)
                : default;

            if (masalah is null && item is not null && !quote.FitsInColumn())
            {
                masalah = "Nilai sewanya melebihi batas yang dapat diproses. Pilih rentang lebih pendek.";
            }

            if (masalah is null && item is not null)
            {
                siap.Add(new BarisSiap(c.Id, item, c.StartAt, c.EndAt, metode, quote));
            }

            lines.Add(new CheckoutQuoteLineResponse
            {
                CartItemId        = c.Id,
                ItemId            = c.ItemId,
                ItemTitle         = item?.Title ?? string.Empty,
                ItemPhotoUrl      = item?.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).FirstOrDefault(),
                SellerId          = item?.SellerId ?? Guid.Empty,
                SellerName        = item?.Seller?.Name ?? string.Empty,
                StartAt           = c.StartAt,
                EndAt             = c.EndAt,
                Price             = item?.Price ?? 0m,
                PriceUnit         = item?.PriceUnit.ToDbValue() ?? PriceUnits.Day,
                DurationUnits     = masalah is null ? quote.DurationUnits : 0,
                TotalRent         = masalah is null ? quote.TotalRent : 0m,
                DepositAmount     = masalah is null ? quote.DepositAmount : 0m,
                DeliveryFee       = masalah is null ? quote.DeliveryFee : 0m,
                OfferedDeliveryFee = item?.DeliveryFee,
                LineTotal         = masalah is null ? quote.RenterTotal : 0m,
                DeliveryMethod    = metodeDb,
                DeliveryAvailable = melayaniAntar,
                Available         = masalah is null,
                Problem           = masalah
            });
        }

        var quoteResponse = new CheckoutQuoteResponse
        {
            Lines           = lines,
            TotalRent       = lines.Sum(l => l.TotalRent),
            TotalDeposit    = lines.Sum(l => l.DepositAmount),
            TotalDelivery   = lines.Sum(l => l.DeliveryFee),
            TotalServiceFee = lines.Sum(l => l.LineTotal - l.TotalRent - l.DepositAmount - l.DeliveryFee),
            GrandTotal      = lines.Sum(l => l.LineTotal),
            AddressRequired = perluAlamat,
            Address         = alamat?.ToResponse(),
            CanSubmit       = lines.Count > 0 && lines.All(l => l.Available)
        };

        return new Persiapan(null, quoteResponse, alamat, siap);
    }
}
