using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record PayoutAccountResponse
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string ProviderCode { get; init; }

    public required string AccountNumberMasked { get; init; }

    public required string AccountHolder { get; init; }
    public required bool IsDefault { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record CreatePayoutAccountRequest
{
    [Required(ErrorMessage = "Jenis rekening wajib diisi.")]
    public string Kind { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kode bank atau e-wallet wajib diisi.")]
    [StringLength(30, MinimumLength = 2)]
    public string ProviderCode { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nomor rekening wajib diisi.")]
    [StringLength(40, MinimumLength = 4)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "Nomor rekening hanya boleh berisi angka.")]
    public string AccountNumber { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nama pemilik rekening wajib diisi.")]
    [StringLength(120, MinimumLength = 2)]
    public string AccountHolder { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
}
