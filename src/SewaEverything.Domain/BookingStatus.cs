namespace SewaEverything.Domain;

public enum BookingStatus
{
    Pending,

    Confirmed,

    Active,

    Completed,

    Cancelled,

    Disputed
}

public static class BookingStatuses
{
    public const string Pending   = "pending";
    public const string Confirmed = "confirmed";
    public const string Active    = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Disputed  = "disputed";

    public static readonly IReadOnlyList<BookingStatus> Blocking =
        [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Active];

    public static readonly IReadOnlyList<BookingStatus> NonTerminal =
    [
        .. Enum.GetValues<BookingStatus>().Where(s => BookingTransitions.AllowedFrom(s).Count > 0)
    ];

    public static string ToDbValue(this BookingStatus status) => status switch
    {
        BookingStatus.Pending   => Pending,
        BookingStatus.Confirmed => Confirmed,
        BookingStatus.Active    => Active,
        BookingStatus.Completed => Completed,
        BookingStatus.Cancelled => Cancelled,
        BookingStatus.Disputed  => Disputed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "status booking tidak dikenal")
    };

    public static BookingStatus FromDbValue(string value) => value switch
    {
        Pending   => BookingStatus.Pending,
        Confirmed => BookingStatus.Confirmed,
        Active    => BookingStatus.Active,
        Completed => BookingStatus.Completed,
        Cancelled => BookingStatus.Cancelled,
        Disputed  => BookingStatus.Disputed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "status booking tidak dikenal")
    };
}

public static class BookingTransitions
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending]   = [BookingStatus.Confirmed, BookingStatus.Cancelled],
        [BookingStatus.Confirmed] = [BookingStatus.Active, BookingStatus.Cancelled],
        [BookingStatus.Active]    = [BookingStatus.Completed, BookingStatus.Disputed],
        [BookingStatus.Disputed]  = [BookingStatus.Completed],
        [BookingStatus.Completed] = [],
        [BookingStatus.Cancelled] = []
    };

    public static IReadOnlyList<BookingStatus> AllowedFrom(BookingStatus from) =>
        Allowed.TryGetValue(from, out var to) ? to : [];

    public static bool IsAllowed(BookingStatus from, BookingStatus to) =>
        Array.IndexOf(Allowed.TryGetValue(from, out var next) ? next : [], to) >= 0;

    public static string Describe(BookingStatus from)
    {
        var next = AllowedFrom(from);

        return next.Count == 0
            ? $"Booking berstatus '{from.ToDbValue()}' sudah final dan tidak bisa diubah lagi."
            : $"Dari status '{from.ToDbValue()}', booking hanya bisa menjadi " +
              $"{string.Join(" atau ", next.Select(s => $"'{s.ToDbValue()}'"))}.";
    }
}
