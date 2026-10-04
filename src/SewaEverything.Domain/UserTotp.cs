namespace SewaEverything.Domain;

public sealed class UserTotp
{
    public Guid UserId { get; set; }

    public string Secret { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; private set; }

    public long? LastStep { get; private set; }

    public User? User { get; set; }

    public bool IsConfirmed => ConfirmedAt is not null;

    public bool Accepts(long step) => LastStep is not { } terakhir || step > terakhir;

    public void Confirm(DateTime at, long step)
    {
        ConfirmedAt = at;
        LastStep = step;
    }

    public void RecordStep(long step) => LastStep = step;
}
