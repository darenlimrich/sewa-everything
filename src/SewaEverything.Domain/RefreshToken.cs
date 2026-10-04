namespace SewaEverything.Domain;

public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public Guid FamilyId { get; set; }

    public DateTime IssuedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public User? User { get; set; }

    public bool IsLive(DateTime now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;
}

public static class RefreshTokenRevokeReasons
{
    public const string Logout = "logout";

    public const string ReuseDetected = "reuse_detected";

    public const string PasswordReset = "password_reset";
}
