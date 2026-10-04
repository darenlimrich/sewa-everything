using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record AddressResponse
{
    public required Guid Id { get; init; }

    public required string Label { get; init; }

    public required string RecipientName { get; init; }

    public required string Phone { get; init; }

    public required string FullAddress { get; init; }

    public string? Notes { get; init; }

    public required bool IsDefault { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public sealed record SaveAddressRequest
{
    [Required(ErrorMessage = "Nama alamat wajib diisi.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "Nama alamat 2–40 karakter.")]
    public string Label { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nama penerima wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama penerima 2–120 karakter.")]
    public string RecipientName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nomor telepon wajib diisi.")]
    [StringLength(30, MinimumLength = 6, ErrorMessage = "Nomor telepon 6–30 karakter.")]
    public string Phone { get; init; } = string.Empty;

    [Required(ErrorMessage = "Alamat lengkap wajib diisi.")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "Alamat lengkap 10–500 karakter.")]
    public string FullAddress { get; init; } = string.Empty;

    [StringLength(200, ErrorMessage = "Catatan maksimal 200 karakter.")]
    public string? Notes { get; init; }

    public bool IsDefault { get; init; }
}

public static class DeliveryMethodValues
{
    public const string Pickup   = "pickup";
    public const string Delivery = "delivery";

    public static readonly (string Value, string Label)[] Options =
    [
        (Pickup,   "Ambil sendiri"),
        (Delivery, "Diantar pemilik")
    ];

    public static string Label(string value) => value switch
    {
        Pickup   => "Ambil sendiri",
        Delivery => "Diantar pemilik",
        _        => value
    };
}
