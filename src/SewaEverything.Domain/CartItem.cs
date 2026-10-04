namespace SewaEverything.Domain;

public sealed class CartItem
{
    public Guid Id { get; set; }

    public Guid RenterId { get; set; }

    public Guid ItemId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Item? Item { get; set; }
}
