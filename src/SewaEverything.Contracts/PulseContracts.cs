namespace SewaEverything.Contracts;

public sealed record PulseResponse
{
    public DateTime? BookingsStamp { get; init; }

    public DateTime? ItemsStamp { get; init; }

    public DateTime? LedgerStamp { get; init; }

    public required int Unread { get; init; }

    public required int ActionNeeded { get; init; }

    public required int CartCount { get; init; }

    public required int OpenDisputes { get; init; }

    public required int PendingPayouts { get; init; }

    public required int PendingSellers { get; init; }

    public required int SuspendedItems { get; init; }

    public required int PendingItems { get; init; }
}
