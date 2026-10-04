using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record AddCartItemRequest
{
    [Required(ErrorMessage = "Barang wajib dipilih.")]
    public Guid ItemId { get; init; }

    [Required(ErrorMessage = "Tanggal mulai wajib diisi.")]
    public DateTime StartAt { get; init; }

    [Required(ErrorMessage = "Tanggal selesai wajib diisi.")]
    public DateTime EndAt { get; init; }
}

public sealed record CartItemResponse
{
    public required Guid Id { get; init; }

    public required Guid ItemId { get; init; }
    public required string ItemTitle { get; init; }
    public string? ItemPhotoUrl { get; init; }

    public required Guid SellerId { get; init; }
    public required string SellerName { get; init; }

    public required decimal Price { get; init; }
    public required string PriceUnit { get; init; }
    public required decimal DepositAmount { get; init; }

    public decimal? DeliveryFee { get; init; }

    public required DateTime StartAt { get; init; }
    public required DateTime EndAt { get; init; }

    public required bool Available { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public sealed record CartResponse
{
    public required IReadOnlyList<CartItemResponse> Items { get; init; }
    public required int Count { get; init; }
}
