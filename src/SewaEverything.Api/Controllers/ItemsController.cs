using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("items")]
public sealed class ItemsController(SewaDbContext db, TimeProvider clock) : ItemScopedController(db)
{
    private static readonly TimeSpan DefaultCalendarWindow = TimeSpan.FromDays(90);

    private static readonly TimeSpan MaxCalendarWindow = TimeSpan.FromDays(365);

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResponse<ItemSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<ItemSummaryResponse>>> Search(
        [FromQuery] ItemSearchRequest request, CancellationToken ct)
    {
        if (request.Sort is not null && !ItemSortOptions.IsKnown(request.Sort))
        {
            ModelState.AddModelError(nameof(request.Sort),
                $"Urutan hanya boleh '{ItemSortOptions.Relevance}', '{ItemSortOptions.Newest}', " +
                $"'{ItemSortOptions.PriceAsc}', atau '{ItemSortOptions.PriceDesc}'.");
        }

        if (request.PriceUnit is not null && !PriceUnits.IsKnown(request.PriceUnit))
        {
            ModelState.AddModelError(nameof(request.PriceUnit),
                $"Satuan harga hanya boleh '{PriceUnits.Hour}', '{PriceUnits.Day}', " +
                $"'{PriceUnits.Week}', atau '{PriceUnits.Month}'.");
        }

        if (request.MinPrice is { } lo && request.MaxPrice is { } hi && lo > hi)
        {
            ModelState.AddModelError(nameof(request.MinPrice),
                "Harga minimum tidak boleh lebih besar dari harga maksimum.");
        }

        if (request.AvailableFrom is null != request.AvailableTo is null)
        {
            ModelState.AddModelError(nameof(request.AvailableFrom),
                "availableFrom dan availableTo harus diisi berpasangan.");
        }
        else if (request.AvailableFrom is { } af && request.AvailableTo is { } at && af >= at)
        {
            ModelState.AddModelError(nameof(request.AvailableTo),
                "availableTo harus setelah availableFrom.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var query = Db.Items
            .AsNoTracking()
            .WherePubliclyVisible();

        var keyword = request.Q?.Trim();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.WhereMatchesText(keyword);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = request.Category.Trim();
            query = query.Where(i => i.Category.ToLower() == category.ToLower());
        }

        if (request.SellerId is { } sellerId)
        {
            query = query.Where(i => i.SellerId == sellerId);
        }

        if (request.MinPrice is { } min)
        {
            query = query.Where(i => i.Price >= min);
        }

        if (request.MaxPrice is { } max)
        {
            query = query.Where(i => i.Price <= max);
        }

        if (request.PriceUnit is not null)
        {
            var unit = PriceUnits.FromDbValue(request.PriceUnit);
            query = query.Where(i => i.PriceUnit == unit);
        }

        if (request.AvailableFrom is { } from && request.AvailableTo is { } to)
        {
            query = query.WhereFreeBetween(Db.ItemBlockedRanges, from.UtcDateTime, to.UtcDateTime);
        }

        var total = await query.CountAsync(ct);

        var sort = request.Sort ?? (string.IsNullOrEmpty(keyword)
            ? ItemSortOptions.Newest
            : ItemSortOptions.Relevance);

        var ordered = sort switch
        {
            ItemSortOptions.PriceAsc  => query.OrderBy(i => i.Price).ThenBy(i => i.Id),
            ItemSortOptions.PriceDesc => query.OrderByDescending(i => i.Price).ThenBy(i => i.Id),

            ItemSortOptions.Relevance when !string.IsNullOrEmpty(keyword)
                => query.OrderByRelevance(keyword).ThenBy(i => i.Id),

            _ => query.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
        };

        var items = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(i => new ItemSummaryResponse
            {
                Id            = i.Id,
                SellerId      = i.SellerId,
                SellerName    = i.Seller!.Name,
                Title         = i.Title,
                Category      = i.Category,
                Price         = i.Price,
                PriceUnit     = i.PriceUnit.ToDbValue(),
                DepositAmount = i.DepositAmount,
                DeliveryFee   = i.DeliveryFee,
                PrimaryPhotoUrl = i.Photos
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                    .Select(p => p.Url)
                    .FirstOrDefault(),
                RatingAverage = Db.Reviews
                    .Where(r => r.Booking!.ItemId == i.Id)
                    .Average(r => (double?)r.Rating),
                RatingCount = Db.Reviews
                    .Count(r => r.Booking!.ItemId == i.Id),
                RentedCount = Db.Bookings
                    .Count(b => b.ItemId == i.Id && b.Status == BookingStatus.Completed),
                CreatedAt = i.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponse<ItemSummaryResponse>
        {
            Items    = items,
            Page     = request.Page,
            PageSize = request.PageSize,
            Total    = total
        });
    }

    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<ItemCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ItemCategoryResponse>>> Categories(
        CancellationToken ct)
    {
        var categories = await Db.Items
            .AsNoTracking()
            .WherePubliclyVisible()
            .GroupBy(i => i.Category.ToLower())
            .Select(g => new ItemCategoryResponse
            {
                Category = g.Min(i => i.Category)!,
                Count    = g.Count(),
                PhotoUrl = g.Min(i => i.Photos
                    .OrderBy(p => p.SortOrder)
                    .Select(p => p.Url)
                    .FirstOrDefault())
            })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Category)
            .ToListAsync(ct);

        return Ok(categories);
    }

    [HttpGet("mine")]
    [Authorize(Roles = Roles.Seller)]
    [ProducesResponseType<IReadOnlyList<ItemSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ItemSummaryResponse>>> Mine(CancellationToken ct)
    {
        var sellerId = HttpContext.User.UserId();

        var items = await Db.Items
            .AsNoTracking()
            .Where(i => i.SellerId == sellerId)
            .OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new ItemSummaryResponse
            {
                Id            = i.Id,
                SellerId      = i.SellerId,
                SellerName    = i.Seller!.Name,
                Title         = i.Title,
                Category      = i.Category,
                Price         = i.Price,
                PriceUnit     = i.PriceUnit.ToDbValue(),
                DepositAmount = i.DepositAmount,
                DeliveryFee   = i.DeliveryFee,
                PrimaryPhotoUrl = i.Photos
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                    .Select(p => p.Url)
                    .FirstOrDefault(),
                Status      = i.Status.ToDbValue(),
                SuspendedAt = i.SuspendedAt,
                ReviewStatus    = i.ReviewStatus.ToDbValue(),
                RejectionReason = i.RejectionReason,
                RatingAverage = Db.Reviews
                    .Where(r => r.Booking!.ItemId == i.Id)
                    .Average(r => (double?)r.Rating),
                RatingCount = Db.Reviews
                    .Count(r => r.Booking!.ItemId == i.Id),
                RentedCount = Db.Bookings
                    .Count(b => b.ItemId == i.Id && b.Status == BookingStatus.Completed),
                CreatedAt   = i.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ItemDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemDetailResponse>> Detail(
        Guid id,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var windowFrom = from?.UtcDateTime ?? now;
        var windowTo   = to?.UtcDateTime ?? windowFrom + DefaultCalendarWindow;

        if (windowTo <= windowFrom)
        {
            ModelState.AddModelError(nameof(to), "Akhir rentang kalender harus setelah awalnya.");
            return ValidationProblem(ModelState);
        }

        if (windowTo - windowFrom > MaxCalendarWindow)
        {
            ModelState.AddModelError(nameof(to),
                $"Rentang kalender maksimal {MaxCalendarWindow.TotalDays:0} hari sekali minta.");
            return ValidationProblem(ModelState);
        }

        var item = await Db.Items
            .AsNoTracking()
            .Include(i => i.Seller)
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (item is null || !CanView(item))
        {
            return Problem(
                title: "Barang tidak ditemukan",
                detail: $"Tidak ada barang dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var blocked = await Db.ItemBlockedRanges
            .AsNoTracking()
            .Where(r => r.ItemId == id && r.StartsAt < windowTo && r.EndsAt > windowFrom)
            .OrderBy(r => r.StartsAt)
            .ToListAsync(ct);

        return Ok(item.ToDetailResponse(blocked, windowFrom, windowTo));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Seller)]
    [ProducesResponseType<ItemDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ItemDetailResponse>> Create(
        CreateItemRequest request, CancellationToken ct)
    {
        if (!PriceUnits.IsKnown(request.PriceUnit))
        {
            ModelState.AddModelError(nameof(request.PriceUnit),
                $"Satuan harga hanya boleh '{PriceUnits.Hour}', '{PriceUnits.Day}', " +
                $"'{PriceUnits.Week}', atau '{PriceUnits.Month}'.");
            return ValidationProblem(ModelState);
        }

        var item = new Item
        {
            SellerId      = HttpContext.User.UserId(),
            Title         = request.Title.Trim(),
            Category      = request.Category.Trim(),
            Description   = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Price         = request.Price,
            PriceUnit     = PriceUnits.FromDbValue(request.PriceUnit),
            DepositAmount = request.DepositAmount,
            DeliveryFee   = request.DeliveryFee,
            Status        = ItemStatus.Active
        };

        Db.Items.Add(item);
        await Db.SaveChangesAsync(ct);

        await Db.Entry(item).Reference(i => i.Seller).LoadAsync(ct);

        var response = item.ToDetailResponse([], item.CreatedAt, item.CreatedAt);

        return CreatedAtAction(nameof(Detail), new { id = item.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Seller)]
    [ProducesResponseType<ItemDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemDetailResponse>> Update(
        Guid id, UpdateItemRequest request, CancellationToken ct)
    {
        if (!PriceUnits.IsKnown(request.PriceUnit))
        {
            ModelState.AddModelError(nameof(request.PriceUnit),
                $"Satuan harga hanya boleh '{PriceUnits.Hour}', '{PriceUnits.Day}', " +
                $"'{PriceUnits.Week}', atau '{PriceUnits.Month}'.");
        }

        if (!ItemStatuses.IsKnown(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status),
                $"Status hanya boleh '{ItemStatuses.Active}' atau '{ItemStatuses.Inactive}'.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var (item, error) = await LoadOwnedItemAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var title       = request.Title.Trim();
        var category    = request.Category.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var unit        = PriceUnits.FromDbValue(request.PriceUnit);

        var kontenBerubah = item!.Title != title
                            || item.Category != category
                            || item.Description != description;

        var syaratBerubah = item.Price != request.Price
                            || item.PriceUnit != unit
                            || item.DepositAmount != request.DepositAmount
                            || item.DeliveryFee != request.DeliveryFee;

        item.Title          = title;
        item.Category       = category;
        item.Description    = description;
        item.Price          = request.Price;
        item.PriceUnit      = unit;
        item.DepositAmount  = request.DepositAmount;
        item.DeliveryFee    = request.DeliveryFee;
        item.Status         = ItemStatuses.FromDbValue(request.Status);

        if (kontenBerubah)
        {
            item.ContentChanged();
        }
        else if (syaratBerubah)
        {
            item.Revised();
        }

        await Db.SaveChangesAsync(ct);

        await Db.Entry(item).Reference(i => i.Seller).LoadAsync(ct);
        await Db.Entry(item).Collection(i => i.Photos).LoadAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;

        var blocked = await Db.ItemBlockedRanges
            .AsNoTracking()
            .Where(r => r.ItemId == id && r.StartsAt < now + DefaultCalendarWindow && r.EndsAt > now)
            .OrderBy(r => r.StartsAt)
            .ToListAsync(ct);

        return Ok(item.ToDetailResponse(blocked, now, now + DefaultCalendarWindow));
    }
}
