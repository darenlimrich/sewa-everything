namespace SewaEverything.Domain;

public enum DisputeStatus
{
    Open,

    Resolved
}

public static class DisputeStatuses
{
    public const string Open     = "open";
    public const string Resolved = "resolved";

    public static string ToDbValue(this DisputeStatus s) => s switch
    {
        DisputeStatus.Open     => Open,
        DisputeStatus.Resolved => Resolved,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "status sengketa tidak dikenal")
    };

    public static DisputeStatus FromDbValue(string value) => value switch
    {
        Open     => DisputeStatus.Open,
        Resolved => DisputeStatus.Resolved,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "status sengketa tidak dikenal")
    };
}

public sealed class Dispute
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Guid RaisedBy { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DisputeStatus Status { get; set; }

    public string? Resolution { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Booking? Booking { get; set; }
}
