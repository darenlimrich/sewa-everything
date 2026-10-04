namespace SewaEverything.Domain;

public enum BlockedRangeSource
{
    Booking,

    Blackout
}

public static class BlockedRangeSources
{
    public const string Booking  = "booking";
    public const string Blackout = "blackout";

    public static string ToDbValue(this BlockedRangeSource source) => source switch
    {
        BlockedRangeSource.Booking  => Booking,
        BlockedRangeSource.Blackout => Blackout,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "sumber blokir tidak dikenal")
    };

    public static BlockedRangeSource FromDbValue(string value) => value switch
    {
        Booking  => BlockedRangeSource.Booking,
        Blackout => BlockedRangeSource.Blackout,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "sumber blokir tidak dikenal")
    };
}

public sealed class ItemBlockedRange
{
    public Guid ItemId { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public BlockedRangeSource Source { get; set; }

    public Guid SourceId { get; set; }
}
