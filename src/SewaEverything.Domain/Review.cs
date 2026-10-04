namespace SewaEverything.Domain;

public sealed class Review
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public Booking? Booking { get; set; }
}
