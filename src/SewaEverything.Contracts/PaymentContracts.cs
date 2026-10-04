using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record PayBookingRequest
{
    [Required(ErrorMessage = "Cara pembayaran wajib dipilih.")]
    public string Channel { get; init; } = string.Empty;
}

public sealed record PaymentInstructionResponse
{
    public required Guid BookingId { get; init; }

    public required string OrderId { get; init; }

    public required string Channel { get; init; }

    public required decimal Amount { get; init; }

    public required string Status { get; init; }

    public string? VirtualAccountNumber { get; init; }
    public string? PaymentCode { get; init; }
    public string? QrString { get; init; }
    public string? QrImageUrl { get; init; }
    public string? RedirectUrl { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public sealed record PaymentResponse
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required Guid BookingId { get; init; }

    public required string Kind { get; init; }

    public required string Direction { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
    public required string Status { get; init; }
    public required string Method { get; init; }
    public string? Channel { get; init; }
    public DateTime? SettledAt { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record BookingLedgerResponse
{
    public required Guid BookingId { get; init; }

    public required decimal AmountDue { get; init; }

    public required decimal AmountSettled { get; init; }

    public required decimal AmountRefundPending { get; init; }

    public required bool IsSettled { get; init; }

    public required IReadOnlyList<PaymentResponse> Entries { get; init; }
}
