using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Infrastructure.Auth;

public sealed class LoginLockoutOptions
{
    public const string SectionName = "Auth:LoginLockout";

    public bool Enabled { get; set; } = true;

    [Range(1, 100, ErrorMessage = "Auth:LoginLockout:MaxAttempts harus antara 1 dan 100.")]
    public int MaxAttempts { get; set; } = 3;

    [Range(1, 1440, ErrorMessage = "Auth:LoginLockout:LockMinutes harus antara 1 dan 1440.")]
    public int LockMinutes { get; set; } = 15;

    [Range(1, 1440, ErrorMessage = "Auth:LoginLockout:WindowMinutes harus antara 1 dan 1440.")]
    public int WindowMinutes { get; set; } = 15;

    public TimeSpan Lock => TimeSpan.FromMinutes(LockMinutes);

    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);
}
