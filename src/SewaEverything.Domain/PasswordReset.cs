namespace SewaEverything.Domain;

public sealed class PasswordReset
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; private set; }

    public User? User { get; set; }

    public bool IsLive(DateTime now) => UsedAt is null && ExpiresAt > now;
}
