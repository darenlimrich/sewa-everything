using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Contracts;

public sealed record CreateReviewRequest
{
    [Range(1, 5, ErrorMessage = "Rating harus antara 1 dan 5.")]
    public int Rating { get; init; }

    [StringLength(2000, ErrorMessage = "Komentar maksimal 2000 karakter.")]
    public string? Comment { get; init; }
}

public sealed record ReviewResponse
{
    public required Guid Id { get; init; }
    public required Guid BookingId { get; init; }

    public required Guid ItemId { get; init; }

    public required int Rating { get; init; }
    public string? Comment { get; init; }

    public required string ReviewerName { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public sealed record RatingBucket
{
    public required int Rating { get; init; }

    public required int Count { get; init; }
}

public sealed record ItemReviewsResponse
{
    public required Guid ItemId { get; init; }

    public required int Count { get; init; }

    public required double? Average { get; init; }

    public required IReadOnlyList<RatingBucket> Buckets { get; init; }

    public int? Rating { get; init; }

    public required IReadOnlyList<ReviewResponse> Items { get; init; }

    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
