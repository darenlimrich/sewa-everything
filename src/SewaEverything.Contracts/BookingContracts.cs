using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record BookingResponse
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required Guid ItemId { get; init; }
    public required string ItemTitle { get; init; }
    public string? ItemPhotoUrl { get; init; }
    public required Guid SellerId { get; init; }
    public required string SellerName { get; init; }

    public required Guid RenterId { get; init; }
    public required string RenterName { get; init; }

    public required DateTime StartsAt { get; init; }

    public required DateTime EndsAt { get; init; }

    public required string Status { get; init; }

    public required decimal PriceSnapshot { get; init; }
    public required string PriceUnitSnapshot { get; init; }

    public required int DurationUnits { get; init; }

    public required decimal TotalRent { get; init; }
    public required decimal DepositAmount { get; init; }

    public required decimal PlatformFeeRate { get; init; }

    public required string PlatformFeeMode { get; init; }

    public required decimal PlatformFeeAmount { get; init; }

    public required string DeliveryMethod { get; init; }

    public required decimal DeliveryFee { get; init; }

    public string? DeliveryRecipient { get; init; }

    public string? DeliveryPhone { get; init; }

    public string? DeliveryAddress { get; init; }

    public string? DeliveryNotes { get; init; }

    public required decimal RenterTotal { get; init; }

    public required decimal SellerGross { get; init; }

    public DateTime? HoldExpiresAt { get; init; }

    public string? CancelledReason { get; init; }

    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed record CreateBookingRequest
{
    [Required(ErrorMessage = "Barang yang disewa wajib diisi.")]
    public Guid ItemId { get; init; }

    [Required(ErrorMessage = "Waktu mulai wajib diisi.")]
    public DateTimeOffset StartsAt { get; init; }

    [Required(ErrorMessage = "Waktu selesai wajib diisi.")]
    public DateTimeOffset EndsAt { get; init; }
}

public sealed record CancelBookingRequest
{
    [StringLength(500, ErrorMessage = "Alasan maksimal 500 karakter.")]
    public string? Reason { get; init; }
}

public sealed record RaiseDisputeRequest
{
    [Required(ErrorMessage = "Alasan sengketa wajib diisi.")]
    [StringLength(1000, MinimumLength = 1, ErrorMessage = "Alasan sengketa 1–1000 karakter.")]
    public string Reason { get; init; } = string.Empty;
}

public sealed record BookingSearchRequest
{
    public string? Status { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Halaman dimulai dari 1.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "Ukuran halaman maksimal 100.")]
    public int PageSize { get; init; } = 20;
}
