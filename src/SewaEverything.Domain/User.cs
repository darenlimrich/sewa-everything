namespace SewaEverything.Domain;

public sealed class User
{
    public Guid Id { get; set; }

    public UserRole Role { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public bool IsVerified { get; private set; }

    public DateTime? VerifiedAt { get; private set; }

    public Guid? VerifiedBy { get; private set; }

    public DateTime? DeactivatedAt { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    public int FailedLoginCount { get; private set; }

    public DateTime? LastFailedLoginAt { get; private set; }

    public DateTime? LockedUntil { get; private set; }

    public DateTime? NotificationsSeenAt { get; private set; }

    public void MarkNotificationsSeen(DateTime now) => NotificationsSeenAt = now;

    public bool IsLockedAt(DateTime now) => LockedUntil is { } sampai && sampai > now;

    public void RegisterFailedLogin(DateTime now, TimeSpan window, int threshold, TimeSpan lockFor)
    {
        if (LastFailedLoginAt is { } terakhir && now - terakhir >= window)
        {
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        LastFailedLoginAt = now;

        if (FailedLoginCount >= threshold)
        {
            LockedUntil = now + lockFor;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LastFailedLoginAt = null;
        LockedUntil = null;
    }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public void Deactivate(DateTime at)
    {
        if (Role == UserRole.Owner)
        {
            throw new InvalidOperationException("Akun owner tidak bisa dinonaktifkan.");
        }

        DeactivatedAt = at;
    }

    public void Reactivate() => DeactivatedAt = null;

    public void MarkVerified(Guid verifiedBy, DateTime at)
    {
        IsVerified = true;
        VerifiedAt = at;
        VerifiedBy = verifiedBy;
    }

    public void RevokeVerification()
    {
        IsVerified = false;
        VerifiedAt = null;
        VerifiedBy = null;
    }
}
