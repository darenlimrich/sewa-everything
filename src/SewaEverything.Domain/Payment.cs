namespace SewaEverything.Domain;

public enum PaymentKind
{
    RentCharge,

    DepositCharge,

    PlatformFee,

    DepositForfeit,

    RentRefund,

    DepositRefund,

    SellerPayout,

    DeliveryCharge,

    DeliveryRefund
}

public enum PaymentDirection
{
    In,
    Out,
    Internal
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Expired
}

public enum PaymentMethod
{
    GatewayCharge,

    GatewayReversal,

    Disbursement,

    Internal
}

public static class PaymentKinds
{
    public const string RentCharge     = "rent_charge";
    public const string DepositCharge  = "deposit_charge";
    public const string PlatformFee    = "platform_fee";
    public const string DepositForfeit = "deposit_forfeit";
    public const string RentRefund     = "rent_refund";
    public const string DepositRefund  = "deposit_refund";
    public const string SellerPayout   = "seller_payout";
    public const string DeliveryCharge = "delivery_charge";
    public const string DeliveryRefund = "delivery_refund";

    public static string ToDbValue(this PaymentKind kind) => kind switch
    {
        PaymentKind.RentCharge     => RentCharge,
        PaymentKind.DepositCharge  => DepositCharge,
        PaymentKind.PlatformFee    => PlatformFee,
        PaymentKind.DepositForfeit => DepositForfeit,
        PaymentKind.RentRefund     => RentRefund,
        PaymentKind.DepositRefund  => DepositRefund,
        PaymentKind.SellerPayout   => SellerPayout,
        PaymentKind.DeliveryCharge => DeliveryCharge,
        PaymentKind.DeliveryRefund => DeliveryRefund,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "jenis pembayaran tidak dikenal")
    };

    public static PaymentKind FromDbValue(string value) => value switch
    {
        RentCharge     => PaymentKind.RentCharge,
        DepositCharge  => PaymentKind.DepositCharge,
        PlatformFee    => PaymentKind.PlatformFee,
        DepositForfeit => PaymentKind.DepositForfeit,
        RentRefund     => PaymentKind.RentRefund,
        DepositRefund  => PaymentKind.DepositRefund,
        SellerPayout   => PaymentKind.SellerPayout,
        DeliveryCharge => PaymentKind.DeliveryCharge,
        DeliveryRefund => PaymentKind.DeliveryRefund,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "jenis pembayaran tidak dikenal")
    };

    public static PaymentKind? TryFromDbValue(string? value) => value switch
    {
        RentCharge     => PaymentKind.RentCharge,
        DepositCharge  => PaymentKind.DepositCharge,
        PlatformFee    => PaymentKind.PlatformFee,
        DepositForfeit => PaymentKind.DepositForfeit,
        RentRefund     => PaymentKind.RentRefund,
        DepositRefund  => PaymentKind.DepositRefund,
        SellerPayout   => PaymentKind.SellerPayout,
        DeliveryCharge => PaymentKind.DeliveryCharge,
        DeliveryRefund => PaymentKind.DeliveryRefund,
        _ => null
    };

    public static PaymentDirection DirectionOf(this PaymentKind kind) => kind switch
    {
        PaymentKind.RentCharge or PaymentKind.DepositCharge or PaymentKind.DeliveryCharge
            => PaymentDirection.In,

        PaymentKind.RentRefund or PaymentKind.DepositRefund or PaymentKind.DeliveryRefund
            or PaymentKind.SellerPayout
            => PaymentDirection.Out,

        PaymentKind.PlatformFee or PaymentKind.DepositForfeit => PaymentDirection.Internal,

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "jenis pembayaran tidak dikenal")
    };
}

public static class PaymentDirections
{
    public const string In       = "in";
    public const string Out      = "out";
    public const string Internal = "internal";

    public static string ToDbValue(this PaymentDirection d) => d switch
    {
        PaymentDirection.In       => In,
        PaymentDirection.Out      => Out,
        PaymentDirection.Internal => Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(d), d, "arah dana tidak dikenal")
    };

    public static PaymentDirection FromDbValue(string value) => value switch
    {
        In       => PaymentDirection.In,
        Out      => PaymentDirection.Out,
        Internal => PaymentDirection.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "arah dana tidak dikenal")
    };
}

public static class PaymentStatuses
{
    public const string Pending = "pending";
    public const string Paid    = "paid";
    public const string Failed  = "failed";
    public const string Expired = "expired";

    public static string ToDbValue(this PaymentStatus s) => s switch
    {
        PaymentStatus.Pending => Pending,
        PaymentStatus.Paid    => Paid,
        PaymentStatus.Failed  => Failed,
        PaymentStatus.Expired => Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "status pembayaran tidak dikenal")
    };

    public static PaymentStatus FromDbValue(string value) => value switch
    {
        Pending => PaymentStatus.Pending,
        Paid    => PaymentStatus.Paid,
        Failed  => PaymentStatus.Failed,
        Expired => PaymentStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "status pembayaran tidak dikenal")
    };

    public static PaymentStatus? TryFromDbValue(string? value) => value switch
    {
        Pending => PaymentStatus.Pending,
        Paid    => PaymentStatus.Paid,
        Failed  => PaymentStatus.Failed,
        Expired => PaymentStatus.Expired,
        _ => null
    };
}

public static class PaymentMethods
{
    public const string GatewayCharge   = "gateway_charge";
    public const string GatewayReversal = "gateway_reversal";
    public const string Disbursement    = "disbursement";
    public const string Internal        = "internal";

    public static string ToDbValue(this PaymentMethod m) => m switch
    {
        PaymentMethod.GatewayCharge   => GatewayCharge,
        PaymentMethod.GatewayReversal => GatewayReversal,
        PaymentMethod.Disbursement    => Disbursement,
        PaymentMethod.Internal        => Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, "metode pembayaran tidak dikenal")
    };

    public static PaymentMethod FromDbValue(string value) => value switch
    {
        GatewayCharge   => PaymentMethod.GatewayCharge,
        GatewayReversal => PaymentMethod.GatewayReversal,
        Disbursement    => PaymentMethod.Disbursement,
        Internal        => PaymentMethod.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "metode pembayaran tidak dikenal")
    };
}

public sealed class Payment
{
    public Guid Id { get; set; }

    public string Reference { get; private set; } = null!;

    public Guid BookingId { get; set; }

    public PaymentKind Kind { get; set; }

    public PaymentDirection Direction { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "IDR";

    public PaymentStatus Status { get; set; }

    public PaymentMethod Method { get; set; }

    public string? Channel { get; set; }

    public Guid? CounterpartyId { get; set; }

    public Guid? PayoutAccountId { get; set; }

    public string? GatewayRef { get; set; }

    public Guid? ParentId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? GatewayInstructions { get; set; }

    public string? FailureReason { get; set; }

    public DateTime? SettledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
