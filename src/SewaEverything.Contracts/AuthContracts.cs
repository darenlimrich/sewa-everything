using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record RegisterRequest
{
    [Required(ErrorMessage = "Nama wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama harus 2–120 karakter.")]
    public string Name { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    [StrongPassword(EmailProperty = nameof(Email), NameProperty = nameof(Name))]
    public string Password { get; init; } = string.Empty;

    [Phone(ErrorMessage = "Format nomor telepon tidak valid.")]
    [StringLength(30)]
    public string? Phone { get; init; }

    [Required(ErrorMessage = "Role wajib diisi.")]
    public string Role { get; init; } = string.Empty;
}

public sealed record UpdateProfileRequest
{
    [Required(ErrorMessage = "Nama wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama harus 2–120 karakter.")]
    public string Name { get; init; } = string.Empty;

    [Phone(ErrorMessage = "Format nomor telepon tidak valid.")]
    [StringLength(30)]
    public string? Phone { get; init; }
}

public sealed record ChangePasswordRequest
{
    [Required(ErrorMessage = "Kata sandi saat ini wajib diisi.")]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kata sandi baru wajib diisi.")]
    [StrongPassword]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    public string Password { get; init; } = string.Empty;

    [StringLength(20)]
    public string? TotpCode { get; init; }
}

public sealed record ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

public sealed record ResetPasswordRequest
{
    [Required(ErrorMessage = "Tautan atur ulang tidak lengkap.")]
    [StringLength(200)]
    public string Token { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kata sandi baru wajib diisi.")]
    [StrongPassword]
    public string Password { get; init; } = string.Empty;
}

public sealed record RefreshTokenRequest
{
    [Required(ErrorMessage = "Refresh token wajib diisi.")]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record AuthResponse
{
    public required string AccessToken { get; init; }
    public required DateTime ExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime RefreshTokenExpiresAt { get; init; }

    public required UserResponse User { get; init; }
}

public sealed record TotpSetupRequest
{
    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    public string Password { get; init; } = string.Empty;
}

public sealed record TotpSetupResponse
{
    public required string Secret { get; init; }

    public required string Uri { get; init; }
}

public sealed record TotpCodeRequest
{
    [Required(ErrorMessage = "Kode autentikasi wajib diisi.")]
    [StringLength(20)]
    public string Code { get; init; } = string.Empty;
}

public sealed record TotpEnableResponse
{
    public required IReadOnlyList<string> RecoveryCodes { get; init; }
}

public sealed record TotpDisableRequest
{
    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kode autentikasi wajib diisi.")]
    [StringLength(20)]
    public string Code { get; init; } = string.Empty;
}

public sealed record TotpStatusResponse
{
    public required bool Enabled { get; init; }

    public required bool PendingSetup { get; init; }

    public DateTime? EnabledAt { get; init; }

    public required int RecoveryCodesLeft { get; init; }
}

public sealed record UserResponse
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    public string? AvatarUrl { get; init; }

    public required bool IsVerified { get; init; }

    public required DateTime CreatedAt { get; init; }
}
