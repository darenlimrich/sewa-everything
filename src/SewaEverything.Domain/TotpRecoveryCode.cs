namespace SewaEverything.Domain;

public sealed class TotpRecoveryCode
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] CodeHash { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? UsedAt { get; private set; }

    public bool IsLive => UsedAt is null;

    public void MarkUsed(DateTime at) => UsedAt = at;
}
