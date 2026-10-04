using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Infrastructure.Auth;

public sealed class PasswordResetOptions
{
    public const string SectionName = "Auth:PasswordReset";

    [Range(5, 1440, ErrorMessage = "Auth:PasswordReset:LifetimeMinutes harus antara 5 dan 1440.")]
    public int LifetimeMinutes { get; set; } = 60;

    [Range(0, 3600, ErrorMessage = "Auth:PasswordReset:MinIntervalSeconds harus antara 0 dan 3600.")]
    public int MinIntervalSeconds { get; set; } = 120;

    [Range(1, 365, ErrorMessage = "Auth:PasswordReset:RetentionDays harus antara 1 dan 365.")]
    public int RetentionDays { get; set; } = 7;

    [StringLength(200)]
    public string WebBaseUrl { get; set; } = "http://localhost:5003";

    [StringLength(100)]
    public string LinkPath { get; set; } = "/atur-ulang";

    public TimeSpan Lifetime => TimeSpan.FromMinutes(LifetimeMinutes);

    public TimeSpan MinInterval => TimeSpan.FromSeconds(MinIntervalSeconds);
}
