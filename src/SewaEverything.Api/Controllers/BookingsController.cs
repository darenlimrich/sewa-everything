using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Bookings;
using SewaEverything.Infrastructure.Payments;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("bookings")]
[Authorize]
public sealed class BookingsController(
    SewaDbContext db,
    TimeProvider clock,
    PaymentLedger ledger,
    BookingCompletion completion,
    IPaymentGateway gateway) : ControllerBase
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    [HttpPost]
    [Authorize(Roles = Roles.Renter)]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Create(
        CreateBookingRequest request, CancellationToken ct)
    {
        var now  = clock.GetUtcNow().UtcDateTime;
        var from = request.StartsAt.UtcDateTime;
        var to   = request.EndsAt.UtcDateTime;

        if (to <= from)
        {
            ModelState.AddModelError(nameof(request.EndsAt),
                "Waktu selesai harus setelah waktu mulai.");
            return ValidationProblem(ModelState);
        }

        if (from < now - ClockSkew)
        {
            ModelState.AddModelError(nameof(request.StartsAt),
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

        var settings = await LoadSettingsAsync(ct);
        var quote    = BookingCalculator.Quote(item, settings, from, to);

        if (!quote.FitsInColumn())
        {
            ModelState.AddModelError(nameof(request.EndsAt),
                "Nilai sewa untuk rentang sepanjang ini melebihi batas yang bisa diproses. " +
                "Pilih rentang yang lebih pendek.");
            return ValidationProblem(ModelState);
        }

        if (await IsTakenAsync(item.Id, from, to, ct))
        {
            return SlotTaken("Tanggal itu sudah terpakai untuk barang ini.");
        }

        var booking = new Booking
        {
            ItemId            = item.Id,
            RenterId          = HttpContext.User.UserId(),
            Status            = BookingStatus.Pending,
            PriceSnapshot     = quote.PriceSnapshot,
            PriceUnitSnapshot = quote.PriceUnitSnapshot,
            DurationUnits     = quote.DurationUnits,
            TotalRent         = quote.TotalRent,
            DepositAmount     = quote.DepositAmount,
            PlatformFeeRate   = quote.PlatformFeeRate,
            PlatformFeeMode   = quote.PlatformFeeMode,
            PlatformFeeAmount = quote.PlatformFeeAmount,

            HoldExpiresAt = now.AddMinutes(settings.ApprovalMinutes)
        };

        db.Bookings.Add(booking);
        db.SetDuring(booking, from, to);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            db.ChangeTracker.Clear();

            return SlotTaken(pg.ConstraintName == "no_overlap"
                ? "Tanggal itu baru saja diambil penyewa lain."
                : "Tanggal itu diblokir oleh pemilik barang.");
        }

        await LoadRelationsAsync(booking, ct);

        return CreatedAtAction(nameof(Detail), new { id = booking.Id }, booking.ToResponse());
    }

    [HttpGet]
    [ProducesResponseType<PagedResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<BookingResponse>>> List(
        [FromQuery] BookingSearchRequest request, CancellationToken ct)
    {
        if (request.Status is not null && !TryParseStatus(request.Status, out _))
        {
            ModelState.AddModelError(nameof(request.Status),
                $"Status hanya boleh salah satu dari: {string.Join(", ", KnownStatuses)}.");
            return ValidationProblem(ModelState);
        }

        var userId = HttpContext.User.UserId();

        var query = Visible()
            .Where(b => HttpContext.User.Role() == UserRole.Renter
                ? b.RenterId == userId
                : HttpContext.User.Role() == UserRole.Seller
                    ? b.Item!.SellerId == userId
                    : true);

        if (request.Status is not null && TryParseStatus(request.Status, out var status))
        {
            query = query.Where(b => b.Status == status);
        }

        var total = await query.CountAsync(ct);

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Ok(new PagedResponse<BookingResponse>
        {
            Items    = [.. bookings.Select(ApiMappings.ToResponse)],
            Page     = request.Page,
            PageSize = request.PageSize,
            Total    = total
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingResponse>> Detail(Guid id, CancellationToken ct)
    {
        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        return booking is null || !IsParty(booking)
            ? BookingNotFound(id)
            : Ok(booking.ToResponse());
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = Roles.Seller)]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Approve(Guid id, CancellationToken ct)
    {
        var (booking, error) = await LoadForSellerAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.PayoutAccounts.AnyAsync(a => a.UserId == HttpContext.User.UserId(), ct))
        {
            return Problem(
                title: "Rekening pencairan belum ada",
                detail: "Mohon tambahkan rekening pencairan terlebih dahulu.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var settings = await LoadSettingsAsync(ct);

        return (await TransitionAsync(booking!, BookingStatus.Confirmed, ct, apply: b =>
            b.HoldExpiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(settings.PaymentMinutes)))
            .Response;
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Cancel(
        Guid id, CancelBookingRequest request, CancellationToken ct)
    {
        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null || !IsParty(booking))
        {
            return BookingNotFound(id);
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Dibatalkan oleh {HttpContext.User.Role().ToDbValue()}."
            : request.Reason.Trim();

        var (cancelled, response) = await TransitionAsync(booking, BookingStatus.Cancelled, ct,
            apply: b =>
            {
                b.CancelledReason = reason;
                b.HoldExpiresAt   = null;
            },
            checkHoldExpiry: false);

        if (!cancelled)
        {
            return response;
        }

        var account = await ledger.DefaultPayoutAccountAsync(booking.RenterId, ct);

        if ((await ledger.CreateFullRefundAsync(booking, account?.Id, ct)).Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return response;
    }

    [HttpPost("{id:guid}/handover")]
    [Authorize(Roles = Roles.Seller)]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Handover(Guid id, CancellationToken ct)
    {
        var (booking, error) = await LoadForSellerAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        if (!BookingTransitions.IsAllowed(booking!.Status, BookingStatus.Active))
        {
            return TransitionRejected(booking, BookingStatus.Active);
        }

        if (!await ledger.IsSettledAsync(booking, ct))
        {
            var settled = await ledger.SettledInAsync(booking.Id, ct);

            return Problem(
                title: "Pembayaran belum lunas",
                detail: $"Baru Rp {settled:N0} dari Rp " +
                        $"{PaymentLedger.WholeRupiah(booking.RenterTotal):N0} yang diterima. " +
                        "Barang belum boleh diserahkan.",
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        return (await TransitionAsync(booking, BookingStatus.Active, ct, apply: b =>
            b.HoldExpiresAt = null)).Response;
    }

    [HttpPost("{id:guid}/return")]
    [Authorize(Roles = Roles.Seller)]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Return(Guid id, CancellationToken ct)
    {
        var (booking, error) = await LoadForSellerAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var failed = await CompleteAsync(booking!, deduction: 0m, ct);

        return failed ?? Ok(booking!.ToResponse());
    }

    [HttpPost("{id:guid}/dispute")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Dispute(
        Guid id, RaiseDisputeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            ModelState.AddModelError(nameof(request.Reason), "Alasan sengketa wajib diisi.");
            return ValidationProblem(ModelState);
        }

        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null)
        {
            return BookingNotFound(id);
        }

        var userId   = HttpContext.User.UserId();
        var isRenter = booking.RenterId == userId;
        var isSeller = booking.Item?.SellerId == userId;

        if (!isRenter && !isSeller)
        {
            return HttpContext.User.Role() is UserRole.Admin or UserRole.Owner
                ? Problem(
                    title: "Sengketa diajukan oleh pihak sewa",
                    detail: "Sengketa hanya dapat diajukan penyewa atau pemilik barang.",
                    statusCode: StatusCodes.Status403Forbidden)
                : BookingNotFound(id);
        }

        if (!BookingTransitions.IsAllowed(booking.Status, BookingStatus.Disputed))
        {
            return TransitionRejected(booking, BookingStatus.Disputed);
        }

        db.Disputes.Add(new Dispute
        {
            BookingId = booking.Id,
            RaisedBy  = userId,
            Reason    = request.Reason.Trim(),
            Status    = DisputeStatus.Open
        });

        booking.Status = BookingStatus.Disputed;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Sengketa sudah ada",
                detail: "Booking ini sudah punya sengketa yang berjalan.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.CheckViolation })
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Transisi status tidak sah",
                detail: "Status booking ini berubah oleh permintaan lain. Muat ulang lalu coba lagi.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(booking.ToResponse());
    }

    [HttpPost("{id:guid}/pay")]
    [Authorize(Roles = Roles.Renter)]
    [ProducesResponseType<PaymentInstructionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentInstructionResponse>> Pay(
        Guid id, PayBookingRequest request, CancellationToken ct)
    {
        if (!PaymentChannels.IsKnown(request.Channel))
        {
            ModelState.AddModelError(nameof(request.Channel),
                "Cara pembayaran itu tidak didukung.");
            return ValidationProblem(ModelState);
        }

        var channel = PaymentChannels.FromDbValue(request.Channel);

        if (channel.NeedsClientToken())
        {
            ModelState.AddModelError(nameof(request.Channel),
                "Kartu kredit/debit belum dapat dipakai di aplikasi ini. Pilih cara bayar lain.");
            return ValidationProblem(ModelState);
        }

        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null || booking.RenterId != HttpContext.User.UserId())
        {
            return BookingNotFound(id);
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return Problem(
                title: "Booking belum bisa dibayar",
                detail: booking.Status == BookingStatus.Pending
                    ? "Seller belum menyetujui permintaan sewa ini."
                    : $"Booking berstatus '{booking.Status.ToDbValue()}' tidak menerima pembayaran.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (await ledger.IsSettledAsync(booking, ct))
        {
            return Problem(
                title: "Sudah lunas",
                detail: "Pembayaran untuk booking ini sudah diterima.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (!channel.IsReversible())
        {
            if (await ledger.DefaultPayoutAccountAsync(booking.RenterId, ct) is null)
            {
                return Problem(
                    title: "Rekening pengembalian dana belum ada",
                    detail: "Mohon tambahkan rekening pengembalian dana terlebih dahulu.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        var pending = await ledger.PendingChargesAsync(booking.Id, ct);

        if (pending.Count > 0)
        {
            return Ok(BuildInstruction(booking, pending));
        }

        var attempt = await ledger.ChargeAttemptsAsync(booking.Id, ct) + 1;
        var charges = ledger.CreateCharges(booking, channel, attempt);
        var gross   = charges.Sum(c => c.Amount);

        ChargeResult result;

        try
        {
            result = await gateway.ChargeAsync(new ChargeRequest(
                OrderId:       PaymentLedger.OrderId(booking.Id, attempt),
                GrossAmount:   (long)gross,
                Channel:       channel,
                CustomerName:  booking.Renter?.Name ?? "Penyewa",
                CustomerEmail: booking.Renter?.Email ?? "noreply@sewaeverything.local",
                ItemTitle:     booking.Item?.Title ?? "Sewa barang"), ct);
        }
        catch (PaymentGatewayException ex)
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Gateway pembayaran tidak bisa dihubungi",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        charges.First(c => c.Kind == PaymentKind.RentCharge).GatewayInstructions =
            JsonSerializer.Serialize(result);

        await db.SaveChangesAsync(ct);

        return Ok(BuildInstruction(booking, charges));
    }

    [HttpGet("{id:guid}/payment")]
    [ProducesResponseType<PaymentInstructionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentInstructionResponse>> Payment(Guid id, CancellationToken ct)
    {
        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null || !IsParty(booking))
        {
            return BookingNotFound(id);
        }

        var charges = await ledger.LatestChargesAsync(booking.Id, ct);

        if (charges.Count == 0)
        {
            return Problem(
                title: "Belum ada tagihan",
                detail: $"Sewa {booking.Reference} belum punya tagihan.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(BuildInstruction(booking, charges));
    }

    [HttpGet("{id:guid}/ledger")]
    [ProducesResponseType<BookingLedgerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingLedgerResponse>> Ledger(Guid id, CancellationToken ct)
    {
        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null || !IsParty(booking))
        {
            return BookingNotFound(id);
        }

        var entries = await db.Payments
            .AsNoTracking()
            .Where(p => p.BookingId == id)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        return Ok(new BookingLedgerResponse
        {
            BookingId     = id,
            AmountDue     = PaymentLedger.WholeRupiah(booking.RenterTotal),
            AmountSettled = entries
                .Where(p => p.Direction == PaymentDirection.In && p.Status == PaymentStatus.Paid)
                .Sum(p => p.Amount),
            AmountRefundPending = entries
                .Where(p => p.Status == PaymentStatus.Pending &&
                            p.Kind is PaymentKind.RentRefund or PaymentKind.DepositRefund)
                .Sum(p => p.Amount),
            IsSettled = await ledger.IsSettledAsync(booking, ct),
            Entries   = [.. entries.Select(ApiMappings.ToResponse)]
        });
    }

    private static PaymentInstructionResponse BuildInstruction(
        Booking booking, IReadOnlyList<Payment> charges)
    {
        var rent = charges.First(c => c.Kind == PaymentKind.RentCharge);

        var result = rent.GatewayInstructions is { } json
            ? JsonSerializer.Deserialize<ChargeResult>(json)
            : default;

        return new PaymentInstructionResponse
        {
            BookingId            = booking.Id,
            OrderId              = rent.GatewayRef ?? string.Empty,
            Channel              = rent.Channel ?? string.Empty,
            Amount               = charges.Sum(c => c.Amount),
            Status               = rent.Status.ToDbValue(),
            VirtualAccountNumber = result.VirtualAccountNumber,
            PaymentCode          = result.PaymentCode,
            QrString             = result.QrString,
            QrImageUrl           = result.QrImageUrl,
            RedirectUrl          = result.RedirectUrl,
            ExpiresAt            = result.ExpiresAt
        };
    }

    private static readonly string[] KnownStatuses =
    [
        BookingStatuses.Pending, BookingStatuses.Confirmed, BookingStatuses.Active,
        BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.Disputed
    ];

    private static bool TryParseStatus(string raw, out BookingStatus status)
    {
        if (Array.IndexOf(KnownStatuses, raw) >= 0)
        {
            status = BookingStatuses.FromDbValue(raw);
            return true;
        }

        status = default;
        return false;
    }

    private IQueryable<Booking> Visible() => db.Bookings
        .Include(b => b.Item).ThenInclude(i => i!.Seller)
        .Include(b => b.Item).ThenInclude(i => i!.Photos)
        .Include(b => b.Renter);

    private bool IsParty(Booking booking)
    {
        var userId = HttpContext.User.UserId();

        return booking.RenterId == userId
            || booking.Item?.SellerId == userId
            || HttpContext.User.Role() is UserRole.Admin or UserRole.Owner;
    }

    private async Task<(Booking? Booking, ActionResult? Error)> LoadForSellerAsync(
        Guid id, CancellationToken ct)
    {
        var booking = await Visible().FirstOrDefaultAsync(b => b.Id == id, ct);

        if (booking is null)
        {
            return (null, BookingNotFound(id));
        }

        if (booking.Item?.SellerId != HttpContext.User.UserId())
        {
            return (null, Problem(
                title: "Bukan barang Anda",
                detail: "Hanya pemilik barang yang boleh memproses booking ini.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (booking, null);
    }

    private async Task<(bool Ok, ActionResult<BookingResponse> Response)> TransitionAsync(
        Booking booking, BookingStatus target, CancellationToken ct,
        Action<Booking>? apply = null, bool checkHoldExpiry = true)
    {
        if (!BookingTransitions.IsAllowed(booking.Status, target))
        {
            return (false, TransitionRejected(booking, target));
        }

        if (checkHoldExpiry
            && booking.HoldExpiresAt is { } expiry
            && expiry < clock.GetUtcNow().UtcDateTime)
        {
            return (false, Problem(
                title: "Hold sudah kedaluwarsa",
                detail: "Batas waktu booking ini sudah lewat dan slotnya akan segera dilepas. " +
                        "Minta penyewa memesan ulang.",
                statusCode: StatusCodes.Status409Conflict));
        }

        booking.Status = target;
        apply?.Invoke(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState == PostgresErrorCodes.CheckViolation)
        {
            db.ChangeTracker.Clear();

            return (false, Problem(
                title: "Transisi status tidak sah",
                detail: "Status booking ini berubah oleh permintaan lain. Muat ulang lalu coba lagi.",
                statusCode: StatusCodes.Status409Conflict));
        }

        return (true, Ok(booking.ToResponse()));
    }

    private async Task<ActionResult?> CompleteAsync(Booking booking, decimal deduction, CancellationToken ct)
    {
        if (!BookingTransitions.IsAllowed(booking.Status, BookingStatus.Completed))
        {
            return TransitionRejected(booking, BookingStatus.Completed);
        }

        try
        {
            await completion.CreateSettlementAsync(booking, deduction, ct);
        }
        catch (InvalidOperationException ex)
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Pencairan tidak bisa diproses",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }

        booking.Status = BookingStatus.Completed;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && pg.SqlState is PostgresErrorCodes.CheckViolation
                                                          or PostgresErrorCodes.UniqueViolation)
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Penyelesaian tidak bisa diproses",
                detail: "Status booking ini berubah oleh permintaan lain. Muat ulang lalu coba lagi.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }

    private ObjectResult TransitionRejected(Booking booking, BookingStatus target) => Problem(
        title: "Transisi status tidak sah",
        detail: $"Booking ini berstatus '{booking.Status.ToDbValue()}' dan tidak bisa " +
                $"menjadi '{target.ToDbValue()}'. {BookingTransitions.Describe(booking.Status)}",
        statusCode: StatusCodes.Status409Conflict);

    private Task<bool> IsTakenAsync(Guid itemId, DateTime from, DateTime to, CancellationToken ct) =>
        db.ItemBlockedRanges
            .AsNoTracking()
            .AnyAsync(r => r.ItemId == itemId && r.StartsAt < to && r.EndsAt > from, ct);

    private async Task LoadRelationsAsync(Booking booking, CancellationToken ct)
    {
        await db.Entry(booking).Reference(b => b.Item).LoadAsync(ct);
        await db.Entry(booking).Reference(b => b.Renter).LoadAsync(ct);

        if (booking.Item is not null)
        {
            await db.Entry(booking.Item).Reference(i => i.Seller).LoadAsync(ct);
            await db.Entry(booking.Item).Collection(i => i.Photos).LoadAsync(ct);
        }
    }

    private async Task<PlatformSettings> LoadSettingsAsync(CancellationToken ct) =>
        await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException(
            "Baris platform_settings tidak ada. Terapkan db/migrations/*.sql dulu.");

    private ObjectResult SlotTaken(string detail) => Problem(
        title: "Slot sudah terpakai",
        detail: detail,
        statusCode: StatusCodes.Status409Conflict);

    private ObjectResult BookingNotFound(Guid id) => Problem(
        title: "Booking tidak ditemukan",
        detail: $"Tidak ada booking dengan id {id} yang bisa Anda lihat.",
        statusCode: StatusCodes.Status404NotFound);
}
