using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Infrastructure.Auth;

public sealed class TotpOptions
{
    public const string SectionName = "Auth:Totp";

    [StringLength(60, MinimumLength = 1)]
    public string Issuer { get; set; } = "Sewaku";

    [Range(0, 3, ErrorMessage = "Auth:Totp:WindowSteps harus antara 0 dan 3.")]
    public int WindowSteps { get; set; } = 1;

    [Range(4, 20, ErrorMessage = "Auth:Totp:RecoveryCodeCount harus antara 4 dan 20.")]
    public int RecoveryCodeCount { get; set; } = 10;
}
