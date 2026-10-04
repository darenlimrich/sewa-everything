using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Bookings;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminController(
    SewaDbContext db, TimeProvider clock, BookingCompletion completion) : ControllerBase
{
    [HttpGet("sellers/pending")]
    [ProducesResponseType<IReadOnlyList<PendingSellerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PendingSellerResponse>>> PendingSellers(
        CancellationToken ct)
    {
        var sellers = await db.Users
            .Where(u => u.Role == UserRole.Seller && !u.IsVerified)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new PendingSellerResponse
            {
                Id        = u.Id,
                Name      = u.Name,
                Email     = u.Email,
                Phone     = u.Phone,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(sellers);
    }

    [HttpGet("sellers")]
    [ProducesResponseType<IReadOnlyList<AdminSellerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdminSellerResponse>>> Sellers(
        [FromQuery] bool? verified, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Seller);

        if (verified is { } wanted)
        {
            query = query.Where(u => u.IsVerified == wanted);
        }

        var sellers = await query
            .OrderByDescending(u => u.VerifiedAt)
            .ThenBy(u => u.CreatedAt)
            .Select(u => new AdminSellerResponse
            {
                Id         = u.Id,
                Name       = u.Name,
                Email      = u.Email,
                Phone      = u.Phone,
                IsVerified = u.IsVerified,
                VerifiedAt = u.VerifiedAt,
                CreatedAt  = u.CreatedAt,
                ActiveItemCount = db.Items
                    .Count(i => i.SellerId == u.Id && i.Status == ItemStatus.Active)
            })
            .ToListAsync(ct);

        return Ok(sellers);
    }

    [HttpPost("sellers/{id:guid}/verify")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> VerifySeller(
        Guid id, VerifySellerRequest request, CancellationToken ct)
    {
        var seller = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (seller is null)
        {
            return Problem(
                title: "Seller tidak ditemukan",
                detail: $"Tidak ada akun dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (seller.Role != UserRole.Seller)
        {
            return Problem(
                title: "Akun ini bukan seller",
                detail: $"Verifikasi dokumen hanya berlaku untuk seller. Akun ini ber-role " +
                        $"'{seller.Role.ToDbValue()}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Approve)
        {
            seller.MarkVerified(HttpContext.User.UserId(), clock.GetUtcNow().UtcDateTime);
        }
        else
        {
            seller.RevokeVerification();
        }

        await db.SaveChangesAsync(ct);

        return Ok(seller.ToResponse());
    }

    [HttpGet("items")]
    [ProducesResponseType<PagedResponse<ModeratedItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<ModeratedItemResponse>>> Items(
        [FromQuery] string? q,
        [FromQuery] bool? suspended,
        [FromQuery] string? review,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page     = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (review is not null && !ItemReviewStatuses.IsKnown(review))
        {
            ModelState.AddModelError(nameof(review),
                $"review hanya boleh '{ItemReviewStatuses.Pending}', '{ItemReviewStatuses.Approved}', " +
                $"atau '{ItemReviewStatuses.Rejected}'.");
            return ValidationProblem(ModelState);
        }

        var query = db.Items.AsNoTracking().AsQueryable();

        var keyword = q?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.WhereMatchesText(keyword);
        }

        if (suspended is { } wanted)
        {
            query = wanted
                ? query.Where(i => i.SuspendedAt != null)
                : query.Where(i => i.SuspendedAt == null);
        }

        if (review is not null)
        {
            var status = ItemReviewStatuses.FromDbValue(review);
            query = query.Where(i => i.ReviewStatus == status);
        }

        var total = await query.CountAsync(ct);

        var ordered = review == ItemReviewStatuses.Pending
            ? query.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            : query
                .OrderBy(i => i.ReviewStatus == ItemReviewStatus.Pending ? 0 : 1)
                .ThenByDescending(i => i.SuspendedAt)
                .ThenByDescending(i => i.CreatedAt);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new ModeratedItemResponse
            {
                Id        = i.Id,
                Title     = i.Title,
                Category  = i.Category,
                Price     = i.Price,
                PriceUnit = i.PriceUnit.ToDbValue(),
                Status    = i.Status.ToDbValue(),

                SellerId         = i.SellerId,
                SellerName       = i.Seller!.Name,
                SellerIsVerified = i.Seller!.IsVerified,

                SuspendedAt      = i.SuspendedAt,
                SuspensionReason = i.SuspensionReason,
                SuspendedByName  = db.Users
                    .Where(u => u.Id == i.SuspendedBy)
                    .Select(u => u.Name)
                    .FirstOrDefault(),

                ReviewStatus    = i.ReviewStatus.ToDbValue(),
                ReviewedAt      = i.ReviewedAt,
                ReviewedByName  = db.Users
                    .Where(u => u.Id == i.ReviewedBy)
                    .Select(u => u.Name)
                    .FirstOrDefault(),
                RejectionReason = i.RejectionReason,
                Description     = i.Description,
                PhotoCount      = i.Photos.Count,

                IsPubliclyVisible = i.Status == ItemStatus.Active
                                    && i.SuspendedAt == null
                                    && i.ReviewStatus == ItemReviewStatus.Approved
                                    && i.Seller!.IsVerified,

                PrimaryPhotoUrl = i.Photos
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                    .Select(p => p.Url)
                    .FirstOrDefault(),

                ActiveBookingCount = db.Bookings.Count(b => b.ItemId == i.Id
                                                            && b.Status != BookingStatus.Completed
                                                            && b.Status != BookingStatus.Cancelled),

                CreatedAt = i.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponse<ModeratedItemResponse>
        {
            Items = items, Page = page, PageSize = pageSize, Total = total
        });
    }

    [HttpPost("items/{id:guid}/suspend")]
    [ProducesResponseType<ModeratedItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModeratedItemResponse>> SuspendItem(
        Guid id, SuspendItemRequest request, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.Seller)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (item is null)
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        item.Suspend(HttpContext.User.UserId(), request.Reason.Trim(), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        return Ok(await DescribeAsync(item, ct));
    }

    [HttpPost("items/{id:guid}/unsuspend")]
    [ProducesResponseType<ModeratedItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModeratedItemResponse>> UnsuspendItem(Guid id, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.Seller)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (item is null)
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        item.Unsuspend();
        await db.SaveChangesAsync(ct);

        return Ok(await DescribeAsync(item, ct));
    }

    [HttpPost("items/{id:guid}/approve")]
    [ProducesResponseType<ModeratedItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModeratedItemResponse>> ApproveItem(Guid id, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.Seller)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (item is null)
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!item.IsApproved)
        {
            item.Approve(HttpContext.User.UserId(), clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }

        return Ok(await DescribeAsync(item, ct));
    }

    [HttpPost("items/{id:guid}/reject")]
    [ProducesResponseType<ModeratedItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ModeratedItemResponse>> RejectItem(
        Guid id, RejectItemRequest request, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.Seller)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (item is null)
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (item.IsApproved)
        {
            return Problem(
                title: "Listing sudah disetujui",
                detail: "Listing yang sudah tayang tidak ditolak, melainkan diturunkan. Gunakan Turunkan.",
                statusCode: StatusCodes.Status409Conflict);
        }

        item.Reject(HttpContext.User.UserId(), request.Reason.Trim(), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        return Ok(await DescribeAsync(item, ct));
    }

    private async Task<ModeratedItemResponse> DescribeAsync(Item item, CancellationToken ct) => new()
    {
        Id        = item.Id,
        Title     = item.Title,
        Category  = item.Category,
        Price     = item.Price,
        PriceUnit = item.PriceUnit.ToDbValue(),
        Status    = item.Status.ToDbValue(),

        SellerId         = item.SellerId,
        SellerName       = item.Seller?.Name ?? string.Empty,
        SellerIsVerified = item.Seller?.IsVerified ?? false,

        SuspendedAt      = item.SuspendedAt,
        SuspensionReason = item.SuspensionReason,
        SuspendedByName  = item.SuspendedBy is null ? null : await db.Users
            .Where(u => u.Id == item.SuspendedBy)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct),

        ReviewStatus    = item.ReviewStatus.ToDbValue(),
        ReviewedAt      = item.ReviewedAt,
        ReviewedByName  = item.ReviewedBy is null ? null : await db.Users
            .Where(u => u.Id == item.ReviewedBy)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct),
        RejectionReason = item.RejectionReason,
        Description     = item.Description,
        PhotoCount      = await db.ItemPhotos.CountAsync(p => p.ItemId == item.Id, ct),

        IsPubliclyVisible = item.IsPubliclyVisible && (item.Seller?.IsVerified ?? false),

        PrimaryPhotoUrl = await db.ItemPhotos
            .Where(p => p.ItemId == item.Id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Select(p => p.Url)
            .FirstOrDefaultAsync(ct),

        ActiveBookingCount = await db.Bookings.CountAsync(b => b.ItemId == item.Id
                                                              && b.Status != BookingStatus.Completed
                                                              && b.Status != BookingStatus.Cancelled, ct),

        CreatedAt = item.CreatedAt
    };

    [HttpGet("disputes")]
    [ProducesResponseType<IReadOnlyList<DisputeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<DisputeResponse>>> Disputes(CancellationToken ct)
    {
        var open = await db.Disputes
            .AsNoTracking()
            .Where(d => d.Status == DisputeStatus.Open)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new
            {
                Dispute = d,
                Booking = db.Bookings
                    .Where(b => b.Id == d.BookingId)
                    .Select(b => new
                    {
                        b.Reference,
                        ItemTitle  = b.Item!.Title,
                        RenterName = b.Renter!.Name,
                        SellerName = b.Item!.Seller!.Name,
                        SellerId   = b.Item!.SellerId,
                        b.RenterId,
                        b.StartsAt,
                        b.EndsAt
                    })
                    .FirstOrDefault(),
                RaisedByName = db.Users
                    .Where(u => u.Id == d.RaisedBy)
                    .Select(u => u.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var result = new List<DisputeResponse>(open.Count);

        foreach (var row in open)
        {
            var collected = await completion.CollectedDepositAsync(row.Dispute.BookingId, ct);

            var raisedByRole = row.Booking is null ? null
                : row.Dispute.RaisedBy == row.Booking.RenterId ? Roles.Renter
                : row.Dispute.RaisedBy == row.Booking.SellerId ? Roles.Seller
                : null;

            result.Add(ToResponse(row.Dispute, collected) with
            {
                BookingReference = row.Booking?.Reference,
                ItemTitle    = row.Booking?.ItemTitle,
                RenterName   = row.Booking?.RenterName,
                SellerName   = row.Booking?.SellerName,
                RaisedByName = row.RaisedByName,
                RaisedByRole = raisedByRole,
                StartsAt     = row.Booking?.StartsAt,
                EndsAt       = row.Booking?.EndsAt
            });
        }

        return Ok(result);
    }

    [HttpPost("disputes/{id:guid}/resolve")]
    [ProducesResponseType<DisputeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeResponse>> ResolveDispute(
        Guid id, ResolveDisputeRequest request, CancellationToken ct)
    {
        var dispute = await db.Disputes.FirstOrDefaultAsync(d => d.Id == id, ct);

        if (dispute is null)
        {
            return Problem(
                title: "Sengketa tidak ditemukan",
                detail: $"Tidak ada sengketa dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (dispute.Status != DisputeStatus.Open)
        {
            return Problem(
                title: "Sengketa sudah diputus",
                detail: "Sengketa ini sudah pernah diselesaikan dan tidak bisa diputus ulang.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == dispute.BookingId, ct);

        if (booking is null || !BookingTransitions.IsAllowed(booking.Status, BookingStatus.Completed))
        {
            return Problem(
                title: "Booking tidak bisa diselesaikan",
                detail: "Booking sengketa ini tidak berada dalam keadaan yang bisa diselesaikan.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var collectedDeposit = await completion.CollectedDepositAsync(booking.Id, ct);

        if (request.DepositDeduction > collectedDeposit)
        {
            ModelState.AddModelError(nameof(request.DepositDeduction),
                $"Potongan tidak boleh melebihi deposit yang tertagih (Rp {collectedDeposit:N0}).");
            return ValidationProblem(ModelState);
        }

        var now = clock.GetUtcNow().UtcDateTime;

        dispute.Status     = DisputeStatus.Resolved;
        dispute.Resolution = request.Resolution.Trim();
        dispute.ResolvedBy = HttpContext.User.UserId();
        dispute.ResolvedAt = now;

        try
        {
            await completion.CreateSettlementAsync(booking, request.DepositDeduction, ct);
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

        return Ok(ToResponse(dispute, collectedDeposit));
    }

    [HttpGet("payouts/pending")]
    [ProducesResponseType<IReadOnlyList<PendingPayoutResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PendingPayoutResponse>>> PendingPayouts(
        CancellationToken ct)
    {
        var rows = await db.Payments
            .AsNoTracking()
            .Where(p => p.Direction == PaymentDirection.Out && p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new
            {
                Payment     = p,
                Counterparty = db.Users
                    .Where(u => u.Id == p.CounterpartyId)
                    .Select(u => u.Name)
                    .FirstOrDefault(),
                ItemTitle = db.Bookings
                    .Where(b => b.Id == p.BookingId)
                    .Select(b => b.Item!.Title)
                    .FirstOrDefault(),
                BookingReference = db.Bookings
                    .Where(b => b.Id == p.BookingId)
                    .Select(b => b.Reference)
                    .FirstOrDefault(),
                Destination = db.PayoutAccounts
                    .Where(a => a.Id == p.PayoutAccountId)
                    .Select(a => new
                    {
                        a.Kind,
                        a.ProviderCode,
                        a.AccountNumber,
                        a.AccountHolder
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var pending = rows.Select(r => new PendingPayoutResponse
        {
            Id              = r.Payment.Id,
            Reference       = r.Payment.Reference,
            BookingId       = r.Payment.BookingId,
            BookingReference = r.BookingReference,
            Kind            = r.Payment.Kind.ToDbValue(),
            Amount          = r.Payment.Amount,
            Method          = r.Payment.Method.ToDbValue(),
            Channel         = r.Payment.Channel,
            CounterpartyId  = r.Payment.CounterpartyId,
            PayoutAccountId = r.Payment.PayoutAccountId,
            CreatedAt       = r.Payment.CreatedAt,

            CounterpartyName        = r.Counterparty,
            ItemTitle               = r.ItemTitle,
            DestinationKind         = r.Destination?.Kind.ToDbValue(),
            DestinationProvider     = r.Destination?.ProviderCode,
            DestinationNumberMasked = r.Destination is null ? null : ApiMappings.Mask(r.Destination.AccountNumber),
            DestinationHolder       = r.Destination?.AccountHolder
        }).ToList();

        return Ok(pending);
    }

    [HttpPost("payouts/{id:guid}/settle")]
    [ProducesResponseType<PendingPayoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PendingPayoutResponse>> SettlePayout(Guid id, CancellationToken ct)
    {
        var payout = await db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (payout is null || payout.Direction != PaymentDirection.Out)
        {
            return Problem(
                title: "Pencairan tidak ditemukan",
                detail: $"Tidak ada kewajiban pencairan atau refund dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (payout.Status != PaymentStatus.Pending)
        {
            return Problem(
                title: "Sudah diproses",
                detail: $"Baris ini sudah berstatus '{payout.Status.ToDbValue()}' dan tidak bisa " +
                        "diproses ulang.",
                statusCode: StatusCodes.Status409Conflict);
        }

        payout.Status    = PaymentStatus.Paid;
        payout.SettledAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);

        var bookingReference = await db.Bookings
            .Where(b => b.Id == payout.BookingId)
            .Select(b => b.Reference)
            .FirstOrDefaultAsync(ct);

        return Ok(new PendingPayoutResponse
        {
            Id              = payout.Id,
            Reference       = payout.Reference,
            BookingId       = payout.BookingId,
            BookingReference = bookingReference,
            Kind            = payout.Kind.ToDbValue(),
            Amount          = payout.Amount,
            Method          = payout.Method.ToDbValue(),
            Channel         = payout.Channel,
            CounterpartyId  = payout.CounterpartyId,
            PayoutAccountId = payout.PayoutAccountId,
            CreatedAt       = payout.CreatedAt
        });
    }

    private static DisputeResponse ToResponse(Dispute d, decimal depositCollected) => new()
    {
        Id               = d.Id,
        BookingId        = d.BookingId,
        RaisedBy         = d.RaisedBy,
        Reason           = d.Reason,
        Status           = d.Status.ToDbValue(),
        Resolution       = d.Resolution,
        ResolvedBy       = d.ResolvedBy,
        ResolvedAt       = d.ResolvedAt,
        CreatedAt        = d.CreatedAt,
        DepositCollected = depositCollected
    };
}
