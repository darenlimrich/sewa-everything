namespace SewaEverything.Contracts;

public sealed record NotificationResponse
{
    public Guid? BookingId { get; init; }

    public Guid? ItemId { get; init; }

    public string? Reference { get; init; }

    public required string Kind { get; init; }

    public required string Status { get; init; }

    public required string ItemTitle { get; init; }
    public string? ItemPhotoUrl { get; init; }

    public required string CounterpartName { get; init; }

    public decimal? Amount { get; init; }

    public DateTime? HoldExpiresAt { get; init; }

    public required DateTime At { get; init; }

    public required bool Unread { get; init; }
}

public sealed record NotificationListResponse
{
    public required IReadOnlyList<NotificationResponse> Items { get; init; }
    public required int UnreadCount { get; init; }
    public DateTime? SeenAt { get; init; }
}

public static class NotificationKinds
{
    public const string Renter  = "renter";
    public const string Seller  = "seller";
    public const string Listing = "listing";
}
