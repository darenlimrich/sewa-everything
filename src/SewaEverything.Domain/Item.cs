namespace SewaEverything.Domain;

public enum PriceUnit
{
    Hour,
    Day,
    Week,
    Month
}

public static class PriceUnits
{
    public const string Hour  = "hour";
    public const string Day   = "day";
    public const string Week  = "week";
    public const string Month = "month";

    public static string ToDbValue(this PriceUnit unit) => unit switch
    {
        PriceUnit.Hour  => Hour,
        PriceUnit.Day   => Day,
        PriceUnit.Week  => Week,
        PriceUnit.Month => Month,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "satuan harga tidak dikenal")
    };

    public static PriceUnit FromDbValue(string value) => value switch
    {
        Hour  => PriceUnit.Hour,
        Day   => PriceUnit.Day,
        Week  => PriceUnit.Week,
        Month => PriceUnit.Month,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "satuan harga tidak dikenal")
    };

    public static bool IsKnown(string? value) =>
        value is Hour or Day or Week or Month;
}

public enum ItemStatus
{
    Active,
    Inactive
}

public static class ItemStatuses
{
    public const string Active   = "active";
    public const string Inactive = "inactive";

    public static string ToDbValue(this ItemStatus status) => status switch
    {
        ItemStatus.Active   => Active,
        ItemStatus.Inactive => Inactive,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "status item tidak dikenal")
    };

    public static ItemStatus FromDbValue(string value) => value switch
    {
        Active   => ItemStatus.Active,
        Inactive => ItemStatus.Inactive,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "status item tidak dikenal")
    };

    public static bool IsKnown(string? value) => value is Active or Inactive;
}

public enum ItemReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public static class ItemReviewStatuses
{
    public const string Pending  = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static string ToDbValue(this ItemReviewStatus status) => status switch
    {
        ItemReviewStatus.Pending  => Pending,
        ItemReviewStatus.Approved => Approved,
        ItemReviewStatus.Rejected => Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "status peninjauan tidak dikenal")
    };

    public static ItemReviewStatus FromDbValue(string value) => value switch
    {
        Pending  => ItemReviewStatus.Pending,
        Approved => ItemReviewStatus.Approved,
        Rejected => ItemReviewStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "status peninjauan tidak dikenal")
    };

    public static bool IsKnown(string? value) => value is Pending or Approved or Rejected;
}

public sealed class Item
{
    public Guid Id { get; set; }

    public Guid SellerId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public PriceUnit PriceUnit { get; set; }

    public decimal DepositAmount { get; set; }


    public decimal? DeliveryFee { get; set; }

    public ItemStatus Status { get; set; } = ItemStatus.Active;

    public DateTime? SuspendedAt { get; private set; }

    public Guid? SuspendedBy { get; private set; }

    public string? SuspensionReason { get; private set; }

    public bool IsSuspended => SuspendedAt is not null;

    public ItemReviewStatus ReviewStatus { get; private set; } = ItemReviewStatus.Pending;

    public DateTime? ReviewedAt { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    public bool IsApproved => ReviewStatus == ItemReviewStatus.Approved;

    public bool IsPubliclyVisible =>
        Status == ItemStatus.Active && SuspendedAt is null && ReviewStatus == ItemReviewStatus.Approved;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? Seller { get; set; }

    public List<ItemPhoto> Photos { get; } = [];

    public void Suspend(Guid by, string reason, DateTime at)
    {
        SuspendedAt      = at;
        SuspendedBy      = by;
        SuspensionReason = reason;
    }

    public void Unsuspend()
    {
        SuspendedAt      = null;
        SuspendedBy      = null;
        SuspensionReason = null;
    }

    public void Approve(Guid by, DateTime at)
    {
        ReviewStatus    = ItemReviewStatus.Approved;
        ReviewedAt      = at;
        ReviewedBy      = by;
        RejectionReason = null;
    }

    public void Reject(Guid by, string reason, DateTime at)
    {
        ReviewStatus    = ItemReviewStatus.Rejected;
        ReviewedAt      = at;
        ReviewedBy      = by;
        RejectionReason = reason;
    }

    public void ContentChanged()
    {
        if (ReviewStatus != ItemReviewStatus.Pending)
        {
            RequestReview();
        }
    }

    public void Revised()
    {
        if (ReviewStatus == ItemReviewStatus.Rejected)
        {
            RequestReview();
        }
    }

    private void RequestReview()
    {
        ReviewStatus    = ItemReviewStatus.Pending;
        ReviewedAt      = null;
        ReviewedBy      = null;
        RejectionReason = null;
    }
}
