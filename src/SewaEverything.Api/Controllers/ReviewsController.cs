using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ReviewsController(SewaDbContext db) : ItemScopedController(db)
{
    [HttpPost("bookings/{bookingId:guid}/reviews")]
    [Authorize(Roles = Roles.Renter)]
    [ProducesResponseType<ReviewResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewResponse>> Create(
        Guid bookingId, CreateReviewRequest request, CancellationToken ct)
    {
        var booking = await Db.Bookings
            .Include(b => b.Renter)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null || booking.RenterId != HttpContext.User.UserId())
        {
            return BookingNotFound(bookingId);
        }

        if (booking.Status != BookingStatus.Completed)
        {
            return Problem(
                title: "Booking belum bisa dinilai",
                detail: "Hanya sewa yang sudah selesai (completed) yang bisa diberi penilaian.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var review = new Review
        {
            BookingId = bookingId,
            Rating    = request.Rating,
            Comment   = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim()
        };

        Db.Reviews.Add(review);

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            Db.ChangeTracker.Clear();

            return Problem(
                title: "Sudah pernah dinilai",
                detail: "Booking ini sudah punya penilaian — satu sewa hanya bisa dinilai sekali.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var response = ToResponse(review, booking.ItemId, booking.Renter?.Name ?? string.Empty);

        return CreatedAtAction(nameof(ForBooking), new { bookingId }, response);
    }

    [HttpGet("bookings/{bookingId:guid}/review")]
    [ProducesResponseType<ReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewResponse>> ForBooking(Guid bookingId, CancellationToken ct)
    {
        var booking = await Db.Bookings
            .Include(b => b.Item)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null || !IsParty(booking))
        {
            return BookingNotFound(bookingId);
        }

        var review = await Db.Reviews
            .AsNoTracking()
            .Where(r => r.BookingId == bookingId)
            .Select(r => new ReviewResponse
            {
                Id           = r.Id,
                BookingId    = r.BookingId,
                ItemId       = r.Booking!.ItemId,
                Rating       = r.Rating,
                Comment      = r.Comment,
                ReviewerName = r.Booking!.Renter!.Name,
                CreatedAt    = r.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        return review is null
            ? Problem(
                title: "Belum ada penilaian",
                detail: $"Booking {bookingId} belum dinilai.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(review);
    }

    [HttpGet("items/{itemId:guid}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType<ItemReviewsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemReviewsResponse>> ForItem(
        Guid itemId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? rating = null,
        CancellationToken ct = default)
    {
        page     = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (rating is { } bintang && bintang is < 1 or > 5)
        {
            ModelState.AddModelError(nameof(rating), "Saringan bintang hanya boleh 1 sampai 5.");
            return ValidationProblem(ModelState);
        }

        var item = await Db.Items
            .AsNoTracking()
            .Include(i => i.Seller)
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);

        if (item is null || !CanView(item))
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {itemId}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var reviews = Db.Reviews.AsNoTracking().Where(r => r.Booking!.ItemId == itemId);

        var semua = await reviews.CountAsync(ct);

        double? average = semua == 0
            ? null
            : Math.Round(await reviews.AverageAsync(r => (double)r.Rating, ct), 2);

        var terkumpul = await reviews
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var buckets = Enumerable.Range(1, 5)
            .Select(bintang => new RatingBucket
            {
                Rating = bintang,
                Count  = terkumpul.FirstOrDefault(t => t.Rating == bintang)?.Count ?? 0
            })
            .ToList();

        if (rating is { } saring)
        {
            reviews = reviews.Where(r => r.Rating == saring);
        }

        var count = rating is null ? semua : await reviews.CountAsync(ct);

        var items = await reviews
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewResponse
            {
                Id           = r.Id,
                BookingId    = r.BookingId,
                ItemId       = itemId,
                Rating       = r.Rating,
                Comment      = r.Comment,
                ReviewerName = r.Booking!.Renter!.Name,
                CreatedAt    = r.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new ItemReviewsResponse
        {
            ItemId   = itemId,
            Count    = count,
            Average  = average,
            Buckets  = buckets,
            Rating   = rating,
            Items    = items,
            Page     = page,
            PageSize = pageSize
        });
    }

    private bool IsParty(Booking booking)
    {
        var userId = HttpContext.User.UserId();

        return booking.RenterId == userId
            || booking.Item?.SellerId == userId
            || HttpContext.User.Role() is UserRole.Admin or UserRole.Owner;
    }

    private static ReviewResponse ToResponse(Review r, Guid itemId, string reviewerName) => new()
    {
        Id           = r.Id,
        BookingId    = r.BookingId,
        ItemId       = itemId,
        Rating       = r.Rating,
        Comment      = r.Comment,
        ReviewerName = reviewerName,
        CreatedAt    = r.CreatedAt
    };

    private ObjectResult BookingNotFound(Guid id) => Problem(
        title: "Booking tidak ditemukan",
        detail: $"Tidak ada booking dengan id {id} yang bisa Anda lihat.",
        statusCode: StatusCodes.Status404NotFound);
}
