namespace SewaEverything.Domain;

public sealed class Booking
{
    public Guid Id { get; set; }

    public string Reference { get; private set; } = null!;

    public Guid ItemId { get; set; }

    public Guid RenterId { get; set; }

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    public BookingStatus Status { get; set; }

    public decimal PriceSnapshot { get; set; }

    public PriceUnit PriceUnitSnapshot { get; set; }

    public int DurationUnits { get; set; }

    public decimal TotalRent { get; set; }

    public decimal DepositAmount { get; set; }

    public decimal PlatformFeeRate { get; set; }

    public CommissionMode PlatformFeeMode { get; set; }

    public decimal PlatformFeeAmount { get; set; }

    public DeliveryMethod DeliveryMethod { get; set; }

    public decimal DeliveryFee { get; set; }

    public string? DeliveryRecipient { get; set; }

    public string? DeliveryPhone { get; set; }

    public string? DeliveryAddress { get; set; }

    public string? DeliveryNotes { get; set; }

    public decimal RenterTotal { get; private set; }

    public decimal SellerGross { get; private set; }

    public DateTime? HoldExpiresAt { get; set; }

    public string? CancelledReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Item? Item { get; set; }

    public User? Renter { get; set; }
}
