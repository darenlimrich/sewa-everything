using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record PlatformSettingsResponse
{
    public required decimal CommissionRate { get; init; }

    public required string CommissionMode { get; init; }

    public required int ApprovalMinutes { get; init; }

    public required int PaymentMinutes { get; init; }

    public required int ReturnWindowDays { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public sealed record PaymentGatewayResponse
{
    public required string Provider { get; init; }

    public required bool IsConfigured { get; init; }

    public required string Source { get; init; }

    public required bool IsProduction { get; init; }

    public string? ClientKey { get; init; }

    public string? ServerKeyHint { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public static class GatewayCredentialSources
{
    public const string Database      = "database";
    public const string Configuration = "konfigurasi";
    public const string None          = "belum_diatur";
}

public sealed record UpdatePaymentGatewayRequest
{
    [StringLength(200, ErrorMessage = "Server key maksimal 200 karakter.")]
    public string? ServerKey { get; init; }

    [Required(ErrorMessage = "Client key wajib diisi.")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "Client key 8–200 karakter.")]
    public string ClientKey { get; init; } = string.Empty;

    public bool IsProduction { get; init; }
}

public sealed record RevenueSummaryResponse
{
    public required decimal MoneyIn { get; init; }

    public required decimal MoneyOut { get; init; }

    public required decimal CashHeld { get; init; }

    public required decimal PayoutsDue { get; init; }

    public required int PendingPayoutCount { get; init; }

    public required decimal CommissionEarned { get; init; }

    public required decimal CommissionLast30Days { get; init; }

    public required decimal InEscrow { get; init; }

    public required int CompletedBookings { get; init; }
    public required int ActiveBookings { get; init; }

    public required int OpenDisputes { get; init; }

    public required DateTime GeneratedAt { get; init; }
}

public sealed record OwnerTransactionResponse
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required Guid BookingId { get; init; }

    public required string BookingReference { get; init; }

    public required string ItemTitle { get; init; }

    public required string Kind { get; init; }
    public required string Direction { get; init; }
    public required decimal Amount { get; init; }
    public required string Status { get; init; }
    public required string Method { get; init; }
    public string? Channel { get; init; }

    public string? CounterpartyName { get; init; }

    public DateTime? SettledAt { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record OwnerTransactionQuery
{
    public string? Kind { get; init; }

    public string? Status { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Halaman dimulai dari 1.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "Ukuran halaman maksimal 100.")]
    public int PageSize { get; init; } = 20;
}

public sealed record AdminAccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    public required bool IsActive { get; init; }

    public DateTime? DeactivatedAt { get; init; }
    public required DateTime CreatedAt { get; init; }

    public bool TwoFactorEnabled { get; init; }
}

public sealed record CreateAdminRequest
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
}

public sealed record SetAdminAccessRequest
{
    public bool Active { get; init; }
}

public sealed record UpdatePlatformSettingsRequest
{
    [Range(0, 1, ErrorMessage = "Komisi harus antara 0 dan 1 (0.05 = 5%).")]
    public decimal CommissionRate { get; init; }

    [Required(ErrorMessage = "Mode komisi wajib diisi.")]
    public string CommissionMode { get; init; } = string.Empty;

    [Range(1, 1440, ErrorMessage = "Jendela persetujuan seller harus 1–1440 menit.")]
    public int ApprovalMinutes { get; init; }

    [Range(1, 1440, ErrorMessage = "Jendela pembayaran harus 1–1440 menit.")]
    public int PaymentMinutes { get; init; }

    [Range(1, 30, ErrorMessage = "Jendela konfirmasi pengembalian harus 1–30 hari.")]
    public int ReturnWindowDays { get; init; }
}
