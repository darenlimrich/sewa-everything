using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record PendingSellerResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record AdminSellerResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public required bool IsVerified { get; init; }
    public DateTime? VerifiedAt { get; init; }
    public required DateTime CreatedAt { get; init; }

    public required int ActiveItemCount { get; init; }
}

public sealed record VerifySellerRequest
{
    public bool Approve { get; init; } = true;

    [StringLength(500)]
    public string? Note { get; init; }
}

public sealed record ModeratedItemResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required decimal Price { get; init; }
    public required string PriceUnit { get; init; }

    public required string Status { get; init; }

    public required Guid SellerId { get; init; }
    public required string SellerName { get; init; }
    public required bool SellerIsVerified { get; init; }

    public DateTime? SuspendedAt { get; init; }

    public string? SuspensionReason { get; init; }

    public string? SuspendedByName { get; init; }

    public required string ReviewStatus { get; init; }

    public DateTime? ReviewedAt { get; init; }

    public string? ReviewedByName { get; init; }

    public string? RejectionReason { get; init; }

    public string? Description { get; init; }

    public required int PhotoCount { get; init; }

    public required bool IsPubliclyVisible { get; init; }

    public string? PrimaryPhotoUrl { get; init; }

    public required int ActiveBookingCount { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public sealed record SuspendItemRequest
{
    [Required(ErrorMessage = "Alasan penurunan wajib diisi.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Alasan harus 5–1000 karakter.")]
    public string Reason { get; init; } = string.Empty;
}

public sealed record RejectItemRequest
{
    [Required(ErrorMessage = "Alasan penolakan wajib diisi.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Alasan harus 5–1000 karakter.")]
    public string Reason { get; init; } = string.Empty;
}

public sealed record DisputeResponse
{
    public required Guid Id { get; init; }
    public required Guid BookingId { get; init; }

    public string? BookingReference { get; init; }

    public required Guid RaisedBy { get; init; }

    public required string Reason { get; init; }

    public required string Status { get; init; }

    public string? Resolution { get; init; }
    public Guid? ResolvedBy { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public required DateTime CreatedAt { get; init; }

    public required decimal DepositCollected { get; init; }

    public string? ItemTitle { get; init; }

    public string? RenterName { get; init; }
    public string? SellerName { get; init; }

    public string? RaisedByName { get; init; }

    public string? RaisedByRole { get; init; }

    public DateTime? StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
}

public sealed record ResolveDisputeRequest
{
    [Range(typeof(decimal), MoneyLimits.Zero, MoneyLimits.Max, ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Potongan deposit tidak boleh negatif.")]
    public decimal DepositDeduction { get; init; }

    [Required(ErrorMessage = "Catatan keputusan wajib diisi.")]
    [StringLength(1000, MinimumLength = 1, ErrorMessage = "Catatan keputusan 1–1000 karakter.")]
    public string Resolution { get; init; } = string.Empty;
}

public sealed record PendingPayoutResponse
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required Guid BookingId { get; init; }

    public string? BookingReference { get; init; }

    public required string Kind { get; init; }

    public required decimal Amount { get; init; }

    public required string Method { get; init; }

    public string? Channel { get; init; }

    public Guid? CounterpartyId { get; init; }

    public Guid? PayoutAccountId { get; init; }

    public required DateTime CreatedAt { get; init; }

    public string? CounterpartyName { get; init; }

    public string? ItemTitle { get; init; }

    public string? DestinationKind { get; init; }

    public string? DestinationProvider { get; init; }

    public string? DestinationNumberMasked { get; init; }

    public string? DestinationHolder { get; init; }
}
