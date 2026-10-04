namespace SewaEverything.Domain;

public enum PayoutAccountKind
{
    Bank,
    Ewallet
}

public static class PayoutAccountKinds
{
    public const string Bank    = "bank";
    public const string Ewallet = "ewallet";

    public static string ToDbValue(this PayoutAccountKind k) => k switch
    {
        PayoutAccountKind.Bank    => Bank,
        PayoutAccountKind.Ewallet => Ewallet,
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "jenis rekening tidak dikenal")
    };

    public static PayoutAccountKind FromDbValue(string value) => value switch
    {
        Bank    => PayoutAccountKind.Bank,
        Ewallet => PayoutAccountKind.Ewallet,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "jenis rekening tidak dikenal")
    };

    public static bool IsKnown(string? value) => value is Bank or Ewallet;
}

public sealed class PayoutAccount
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public PayoutAccountKind Kind { get; set; }

    public string ProviderCode { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountHolder { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
