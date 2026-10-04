namespace SewaEverything.Domain;

public readonly record struct BookingQuote
{
    public required int DurationUnits { get; init; }
    public required decimal PriceSnapshot { get; init; }
    public required PriceUnit PriceUnitSnapshot { get; init; }
    public required decimal TotalRent { get; init; }
    public required decimal DepositAmount { get; init; }
    public required decimal PlatformFeeRate { get; init; }
    public required CommissionMode PlatformFeeMode { get; init; }
    public required decimal PlatformFeeAmount { get; init; }
    public required DeliveryMethod DeliveryMethod { get; init; }
    public required decimal DeliveryFee { get; init; }

    public decimal RenterTotal =>
        TotalRent + DepositAmount + DeliveryFee
        + (PlatformFeeMode == CommissionMode.OnTop ? PlatformFeeAmount : 0m);

    public decimal SellerGross =>
        TotalRent
        - (PlatformFeeMode == CommissionMode.Deduct ? PlatformFeeAmount : 0m)
        + DeliveryFee;
}

public static class BookingCalculator
{
    public const decimal MaxAmount = 999_999_999_999.99m;

    public static TimeSpan UnitLength(PriceUnit unit) => unit switch
    {
        PriceUnit.Hour  => TimeSpan.FromHours(1),
        PriceUnit.Day   => TimeSpan.FromDays(1),
        PriceUnit.Week  => TimeSpan.FromDays(7),
        PriceUnit.Month => TimeSpan.FromDays(30),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "satuan harga tidak dikenal")
    };

    public static int DurationUnits(PriceUnit unit, DateTime from, DateTime to)
    {
        if (to <= from)
        {
            throw new ArgumentException("Rentang sewa harus berakhir setelah ia dimulai.", nameof(to));
        }

        var span   = (to - from).Ticks;
        var length = UnitLength(unit).Ticks;

        var units = (span + length - 1) / length;

        return (int)Math.Max(1, units);
    }

    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static BookingQuote Quote(Item item, PlatformSettings settings, DateTime from, DateTime to,
                                     DeliveryMethod delivery = DeliveryMethod.Pickup)
    {
        var units     = DurationUnits(item.PriceUnit, from, to);
        var totalRent = item.Price * units;
        var feeAmount = Round2(totalRent * settings.CommissionRate);

        if (delivery == DeliveryMethod.Delivery && item.DeliveryFee is null)
        {
            throw new InvalidOperationException("Barang ini tidak melayani pengantaran.");
        }

        var deliveryFee = delivery == DeliveryMethod.Delivery ? item.DeliveryFee ?? 0m : 0m;

        return new BookingQuote
        {
            DeliveryMethod    = delivery,
            DeliveryFee       = deliveryFee,
            DurationUnits     = units,
            PriceSnapshot     = item.Price,
            PriceUnitSnapshot = item.PriceUnit,
            TotalRent         = totalRent,
            DepositAmount     = item.DepositAmount,
            PlatformFeeRate   = settings.CommissionRate,
            PlatformFeeMode   = settings.CommissionMode,
            PlatformFeeAmount = feeAmount
        };
    }

    public static bool FitsInColumn(this BookingQuote quote) =>
        quote.TotalRent <= MaxAmount
        && quote.DepositAmount <= MaxAmount
        && quote.PlatformFeeAmount <= MaxAmount
        && quote.DeliveryFee <= MaxAmount
        && quote.TotalRent + quote.DepositAmount + quote.PlatformFeeAmount + quote.DeliveryFee <= MaxAmount;
}
