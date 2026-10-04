using System.ComponentModel.DataAnnotations;
using System.Text;

namespace SewaEverything.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "sewa-everything";

    [Required]
    public string Audience { get; set; } = "sewa-everything";

    [Required(ErrorMessage = "Jwt:SigningKey wajib diisi.")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 720, ErrorMessage = "Jwt:AccessTokenHours harus antara 1 dan 720.")]
    public int AccessTokenHours { get; set; } = 12;

    [Range(1, 365, ErrorMessage = "Jwt:RefreshTokenDays harus antara 1 dan 365.")]
    public int RefreshTokenDays { get; set; } = 30;

    public int SigningKeyByteLength => Encoding.UTF8.GetByteCount(SigningKey);
}
