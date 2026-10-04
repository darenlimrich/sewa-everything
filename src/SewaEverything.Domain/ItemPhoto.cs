namespace SewaEverything.Domain;

public sealed class ItemPhoto
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }

    public string Url { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
