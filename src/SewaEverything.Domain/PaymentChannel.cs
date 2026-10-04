namespace SewaEverything.Domain;

public enum PaymentChannel
{
    VaBca,
    VaBni,
    VaBri,
    VaPermata,
    Qris,
    Gopay,
    CstoreAlfamart,
    CstoreIndomaret,
    CreditCard
}

public static class PaymentChannels
{
    public const string VaBca           = "va_bca";
    public const string VaBni           = "va_bni";
    public const string VaBri           = "va_bri";
    public const string VaPermata       = "va_permata";
    public const string Qris            = "qris";
    public const string Gopay           = "gopay";
    public const string CstoreAlfamart  = "cstore_alfamart";
    public const string CstoreIndomaret = "cstore_indomaret";
    public const string CreditCard      = "credit_card";

    public static string ToDbValue(this PaymentChannel c) => c switch
    {
        PaymentChannel.VaBca           => VaBca,
        PaymentChannel.VaBni           => VaBni,
        PaymentChannel.VaBri           => VaBri,
        PaymentChannel.VaPermata       => VaPermata,
        PaymentChannel.Qris            => Qris,
        PaymentChannel.Gopay           => Gopay,
        PaymentChannel.CstoreAlfamart  => CstoreAlfamart,
        PaymentChannel.CstoreIndomaret => CstoreIndomaret,
        PaymentChannel.CreditCard      => CreditCard,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "channel pembayaran tidak dikenal")
    };

    public static PaymentChannel FromDbValue(string value) => value switch
    {
        VaBca           => PaymentChannel.VaBca,
        VaBni           => PaymentChannel.VaBni,
        VaBri           => PaymentChannel.VaBri,
        VaPermata       => PaymentChannel.VaPermata,
        Qris            => PaymentChannel.Qris,
        Gopay           => PaymentChannel.Gopay,
        CstoreAlfamart  => PaymentChannel.CstoreAlfamart,
        CstoreIndomaret => PaymentChannel.CstoreIndomaret,
        CreditCard      => PaymentChannel.CreditCard,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "channel pembayaran tidak dikenal")
    };

    public static bool IsKnown(string? value) =>
        value is VaBca or VaBni or VaBri or VaPermata or Qris or Gopay
              or CstoreAlfamart or CstoreIndomaret or CreditCard;

    public static bool NeedsClientToken(this PaymentChannel channel) =>
        channel is PaymentChannel.CreditCard;

    public static bool IsReversible(this PaymentChannel channel) =>
        channel is PaymentChannel.Gopay or PaymentChannel.CreditCard;

    public static readonly IReadOnlyList<string> ReversibleDbValues = [Gopay, CreditCard];

    public static PaymentMethod RefundMethod(this PaymentChannel channel) =>
        channel.IsReversible() ? PaymentMethod.GatewayReversal : PaymentMethod.Disbursement;
}
