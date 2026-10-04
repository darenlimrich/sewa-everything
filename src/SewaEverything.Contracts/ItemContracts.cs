using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record ItemSummaryResponse
{
    public required Guid Id { get; init; }
    public required Guid SellerId { get; init; }
    public required string SellerName { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }

    public required decimal Price { get; init; }

    public required string PriceUnit { get; init; }

    public required decimal DepositAmount { get; init; }

    public decimal? DeliveryFee { get; init; }

    public string? PrimaryPhotoUrl { get; init; }

    public string? Status { get; init; }

    public DateTime? SuspendedAt { get; init; }

    public string? ReviewStatus { get; init; }

    public string? RejectionReason { get; init; }

    public double? RatingAverage { get; init; }

    public int RatingCount { get; init; }

    public int RentedCount { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public sealed record ItemPhotoResponse
{
    public required Guid Id { get; init; }
    public required string Url { get; init; }
    public required int SortOrder { get; init; }
}

public sealed record BlockedRangeResponse
{
    public required DateTime StartsAt { get; init; }

    public required DateTime EndsAt { get; init; }

    public required string Source { get; init; }
}

public sealed record ItemDetailResponse
{
    public required Guid Id { get; init; }
    public required Guid SellerId { get; init; }
    public required string SellerName { get; init; }
    public required bool SellerIsVerified { get; init; }

    public required string Title { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }

    public required decimal Price { get; init; }
    public required string PriceUnit { get; init; }
    public required decimal DepositAmount { get; init; }

    public decimal? DeliveryFee { get; init; }

    public required string Status { get; init; }

    public DateTime? SuspendedAt { get; init; }

    public string? SuspensionReason { get; init; }

    public required string ReviewStatus { get; init; }

    public DateTime? ReviewedAt { get; init; }

    public string? RejectionReason { get; init; }

    public required IReadOnlyList<ItemPhotoResponse> Photos { get; init; }

    public required IReadOnlyList<BlockedRangeResponse> BlockedRanges { get; init; }

    public required DateTime CalendarFrom { get; init; }
    public required DateTime CalendarTo { get; init; }

    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed record ItemBlackoutResponse
{
    public required Guid Id { get; init; }
    public required DateTime StartsAt { get; init; }

    public required DateTime EndsAt { get; init; }

    public string? Reason { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record PagedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }

    public required int Total { get; init; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public static class MoneyLimits
{
    public const string Zero = "0";

    public const string SmallestPositive = "0.01";

    public const string Max = "999999999999.99";
}

public sealed record CreateItemRequest
{
    [Required(ErrorMessage = "Judul wajib diisi.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Judul harus 3–200 karakter.")]
    public string Title { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kategori wajib diisi.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Kategori harus 2–80 karakter.")]
    public string Category { get; init; } = string.Empty;

    [StringLength(4000, ErrorMessage = "Deskripsi maksimal 4000 karakter.")]
    public string? Description { get; init; }

    [Range(typeof(decimal), MoneyLimits.SmallestPositive, MoneyLimits.Max,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Harga harus lebih dari 0.")]
    public decimal Price { get; init; }

    [Required(ErrorMessage = "Satuan harga wajib diisi.")]
    public string PriceUnit { get; init; } = string.Empty;

    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Deposit tidak boleh negatif.")]
    public decimal DepositAmount { get; init; }

    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Biaya antar tidak boleh negatif.")]
    public decimal? DeliveryFee { get; init; }
}

public sealed record UpdateItemRequest
{
    [Required(ErrorMessage = "Judul wajib diisi.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Judul harus 3–200 karakter.")]
    public string Title { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kategori wajib diisi.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Kategori harus 2–80 karakter.")]
    public string Category { get; init; } = string.Empty;

    [StringLength(4000, ErrorMessage = "Deskripsi maksimal 4000 karakter.")]
    public string? Description { get; init; }

    [Range(typeof(decimal), MoneyLimits.SmallestPositive, MoneyLimits.Max,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Harga harus lebih dari 0.")]
    public decimal Price { get; init; }

    [Required(ErrorMessage = "Satuan harga wajib diisi.")]
    public string PriceUnit { get; init; } = string.Empty;

    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Deposit tidak boleh negatif.")]
    public decimal DepositAmount { get; init; }

    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Biaya antar tidak boleh negatif.")]
    public decimal? DeliveryFee { get; init; }

    [Required(ErrorMessage = "Status wajib diisi.")]
    public string Status { get; init; } = string.Empty;
}

public sealed record CreateBlackoutRequest
{
    [Required(ErrorMessage = "Waktu mulai wajib diisi.")]
    public DateTimeOffset StartsAt { get; init; }

    [Required(ErrorMessage = "Waktu selesai wajib diisi.")]
    public DateTimeOffset EndsAt { get; init; }

    [StringLength(500, ErrorMessage = "Alasan maksimal 500 karakter.")]
    public string? Reason { get; init; }
}

public sealed record ItemSearchRequest
{
    [StringLength(200)]
    public string? Q { get; init; }

    public Guid? SellerId { get; init; }

    [StringLength(80)]
    public string? Category { get; init; }

    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max, ParseLimitsInInvariantCulture = true)]
    public decimal? MinPrice { get; init; }

    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max, ParseLimitsInInvariantCulture = true)]
    public decimal? MaxPrice { get; init; }

    public string? PriceUnit { get; init; }

    public DateTimeOffset? AvailableFrom { get; init; }

    public DateTimeOffset? AvailableTo { get; init; }

    public string? Sort { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Halaman dimulai dari 1.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "Ukuran halaman maksimal 100.")]
    public int PageSize { get; init; } = 20;
}

public sealed record ItemCategoryResponse
{
    public required string Category { get; init; }

    public required int Count { get; init; }

    public string? PhotoUrl { get; init; }
}

public static class ItemSortOptions
{
    public const string Relevance = "relevance";
    public const string Newest    = "newest";
    public const string PriceAsc  = "price_asc";
    public const string PriceDesc = "price_desc";

    public static bool IsKnown(string? value) =>
        value is Relevance or Newest or PriceAsc or PriceDesc;
}
