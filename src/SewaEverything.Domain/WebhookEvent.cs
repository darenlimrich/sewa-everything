namespace SewaEverything.Domain;

public sealed class WebhookEvent
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string? Signature { get; set; }

    public string Payload { get; set; } = "{}";

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? ProcessError { get; set; }
}
