using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record CheckoutLineRequest
{
    [Required(ErrorMessage = "Baris keranjang wajib dipilih.")]
    public Guid CartItemId { get; init; }

    [Required(ErrorMessage = "Cara pengiriman wajib dipilih.")]
    public string DeliveryMethod { get; init; } = DeliveryMethodValues.Pickup;
}

public sealed record CheckoutRequest
{
    [Required(ErrorMessage = "Pilih minimal satu barang.")]
    [MinLength(1, ErrorMessage = "Pilih minimal satu barang.")]
    [MaxLength(20, ErrorMessage = "Maksimal 20 barang sekali ajukan.")]
    public IReadOnlyList<CheckoutLineRequest> Lines { get; init; } = [];

    public Guid? AddressId { get; init; }
}

public sealed record CheckoutQuoteLineResponse
{
    public required Guid CartItemId { get; init; }

    public required Guid ItemId { get; init; }
    public required string ItemTitle { get; init; }
    public string? ItemPhotoUrl { get; init; }

    public required Guid SellerId { get; init; }
    public required string SellerName { get; init; }

    public required DateTime StartAt { get; init; }
    public required DateTime EndAt { get; init; }

    public required decimal Price { get; init; }
    public required string PriceUnit { get; init; }
    public required int DurationUnits { get; init; }

    public required decimal TotalRent { get; init; }
    public required decimal DepositAmount { get; init; }
    public required decimal DeliveryFee { get; init; }
    public decimal? OfferedDeliveryFee { get; init; }
    public required decimal LineTotal { get; init; }

    public required string DeliveryMethod { get; init; }

    public required bool DeliveryAvailable { get; init; }

    public required bool Available { get; init; }

    public string? Problem { get; init; }
}

public sealed record CheckoutQuoteResponse
{
    public required IReadOnlyList<CheckoutQuoteLineResponse> Lines { get; init; }

    public required decimal TotalRent { get; init; }
    public required decimal TotalDeposit { get; init; }
    public required decimal TotalDelivery { get; init; }
    public decimal TotalServiceFee { get; init; }
    public required decimal GrandTotal { get; init; }

    public required bool AddressRequired { get; init; }

    public AddressResponse? Address { get; init; }

    public required bool CanSubmit { get; init; }
}

public sealed record CheckoutResultResponse
{
    public required IReadOnlyList<BookingResponse> Bookings { get; init; }

    public required int Count { get; init; }
}
