namespace SewaEverything.Domain;

public enum CommissionMode
{
    Deduct,

    OnTop
}

public static class CommissionModes
{
    public const string Deduct = "deduct";
    public const string OnTop  = "on_top";

    public static string ToDbValue(this CommissionMode mode) => mode switch
    {
        CommissionMode.Deduct => Deduct,
        CommissionMode.OnTop  => OnTop,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "mode komisi tidak dikenal")
    };

    public static CommissionMode FromDbValue(string value) => value switch
    {
        Deduct => CommissionMode.Deduct,
        OnTop  => CommissionMode.OnTop,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "mode komisi tidak dikenal")
    };
}

public sealed class PlatformSettings
{
    public bool Id { get; set; } = true;

    public decimal CommissionRate { get; set; }

    public CommissionMode CommissionMode { get; set; }

    public int ApprovalMinutes { get; set; }

    public int PaymentMinutes { get; set; }

    public int ReturnWindowDays { get; set; }

    public string? MidtransServerKey { get; set; }

    public string? MidtransClientKey { get; set; }

    public bool MidtransIsProduction { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }
}
