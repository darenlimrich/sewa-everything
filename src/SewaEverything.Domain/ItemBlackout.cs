namespace SewaEverything.Domain;

public sealed class ItemBlackout
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
}
