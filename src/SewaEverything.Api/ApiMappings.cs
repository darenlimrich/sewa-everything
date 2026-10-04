using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Api;

public static class ApiMappings
{
    public static UserResponse ToResponse(this User u) => new()
    {
        Id         = u.Id,
        Role       = u.Role.ToDbValue(),
        Name       = u.Name,
        Email      = u.Email,
        Phone      = u.Phone,
        AvatarUrl  = u.AvatarUrl,
        IsVerified = u.IsVerified,
        CreatedAt  = u.CreatedAt
    };

    public static AdminAccountResponse ToAdminResponse(this User u) => new()
    {
        Id            = u.Id,
        Name          = u.Name,
        Email         = u.Email,
        Phone         = u.Phone,
        IsActive      = u.IsActive,
        DeactivatedAt = u.DeactivatedAt,
        CreatedAt     = u.CreatedAt
    };

    public static PlatformSettingsResponse ToResponse(this PlatformSettings s) => new()
    {
        CommissionRate   = s.CommissionRate,
        CommissionMode   = s.CommissionMode.ToDbValue(),
        ApprovalMinutes  = s.ApprovalMinutes,
        PaymentMinutes   = s.PaymentMinutes,
        ReturnWindowDays = s.ReturnWindowDays,
        UpdatedAt        = s.UpdatedAt
    };

    public static ItemPhotoResponse ToResponse(this ItemPhoto p) => new()
    {
        Id        = p.Id,
        Url       = p.Url,
        SortOrder = p.SortOrder
    };

    public static BlockedRangeResponse ToResponse(this ItemBlockedRange r) => new()
    {
        StartsAt = r.StartsAt,
        EndsAt   = r.EndsAt,
        Source   = r.Source.ToDbValue()
    };

    public static ItemBlackoutResponse ToResponse(this ItemBlackout b) => new()
    {
        Id        = b.Id,
        StartsAt  = b.StartsAt,
        EndsAt    = b.EndsAt,
        Reason    = b.Reason,
        CreatedAt = b.CreatedAt
    };

    public static PaymentResponse ToResponse(this Payment p) => new()
    {
        Id        = p.Id,
        Reference = p.Reference,
        BookingId = p.BookingId,
        Kind      = p.Kind.ToDbValue(),
        Direction = p.Direction.ToDbValue(),
        Amount    = p.Amount,
        Currency  = p.Currency,
        Status    = p.Status.ToDbValue(),
        Method    = p.Method.ToDbValue(),
        Channel   = p.Channel,
        SettledAt = p.SettledAt,
        CreatedAt = p.CreatedAt
    };

    public static PayoutAccountResponse ToResponse(this PayoutAccount a) => new()
    {
        Id           = a.Id,
        Kind         = a.Kind.ToDbValue(),
        ProviderCode = a.ProviderCode,

        AccountNumberMasked = Mask(a.AccountNumber),

        AccountHolder = a.AccountHolder,
        IsDefault     = a.IsDefault,
        CreatedAt     = a.CreatedAt
    };

    public static string Mask(string accountNumber) =>
        accountNumber.Length <= 4
            ? new string('•', accountNumber.Length)
            : new string('•', accountNumber.Length - 4) + accountNumber[^4..];

    public static BookingResponse ToResponse(this Booking b) => new()
    {
        Id         = b.Id,
        Reference  = b.Reference,
        ItemId     = b.ItemId,
        ItemTitle  = b.Item?.Title ?? string.Empty,
        ItemPhotoUrl = b.Item?.Photos
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Select(p => p.Url)
            .FirstOrDefault(),
        SellerId   = b.Item?.SellerId ?? Guid.Empty,
        SellerName = b.Item?.Seller?.Name ?? string.Empty,
        RenterId   = b.RenterId,
        RenterName = b.Renter?.Name ?? string.Empty,

        StartsAt = b.StartsAt,
        EndsAt   = b.EndsAt,
        Status   = b.Status.ToDbValue(),

        PriceSnapshot     = b.PriceSnapshot,
        PriceUnitSnapshot = b.PriceUnitSnapshot.ToDbValue(),
        DurationUnits     = b.DurationUnits,
        TotalRent         = b.TotalRent,
        DepositAmount     = b.DepositAmount,
        PlatformFeeRate   = b.PlatformFeeRate,
        PlatformFeeMode   = b.PlatformFeeMode.ToDbValue(),
        PlatformFeeAmount = b.PlatformFeeAmount,
        DeliveryMethod    = b.DeliveryMethod.ToDbValue(),
        DeliveryFee       = b.DeliveryFee,
        DeliveryRecipient = b.DeliveryRecipient,
        DeliveryPhone     = b.DeliveryPhone,
        DeliveryAddress   = b.DeliveryAddress,
        DeliveryNotes     = b.DeliveryNotes,
        RenterTotal       = b.RenterTotal,
        SellerGross       = b.SellerGross,

        HoldExpiresAt   = b.HoldExpiresAt,
        CancelledReason = b.CancelledReason,
        CreatedAt       = b.CreatedAt,
        UpdatedAt       = b.UpdatedAt
    };

    public static ItemDetailResponse ToDetailResponse(
        this Item item, IReadOnlyList<ItemBlockedRange> blocked, DateTime calendarFrom, DateTime calendarTo) => new()
    {
        Id               = item.Id,
        SellerId         = item.SellerId,
        SellerName       = item.Seller?.Name ?? string.Empty,
        SellerIsVerified = item.Seller?.IsVerified ?? false,
        Title            = item.Title,
        Category         = item.Category,
        Description      = item.Description,
        Price            = item.Price,
        PriceUnit        = item.PriceUnit.ToDbValue(),
        DepositAmount    = item.DepositAmount,
        DeliveryFee      = item.DeliveryFee,
        Status           = item.Status.ToDbValue(),

        SuspendedAt      = item.SuspendedAt,
        SuspensionReason = item.SuspensionReason,

        ReviewStatus     = item.ReviewStatus.ToDbValue(),
        ReviewedAt       = item.ReviewedAt,
        RejectionReason  = item.RejectionReason,

        Photos           = [.. item.Photos.OrderBy(p => p.SortOrder).Select(ToResponse)],
        BlockedRanges    = [.. blocked.Select(ToResponse)],
        CalendarFrom     = calendarFrom,
        CalendarTo       = calendarTo,
        CreatedAt        = item.CreatedAt,
        UpdatedAt        = item.UpdatedAt
    };

    public static AddressResponse ToResponse(this Address a) => new()
    {
        Id            = a.Id,
        Label         = a.Label,
        RecipientName = a.RecipientName,
        Phone         = a.Phone,
        FullAddress   = a.FullAddress,
        Notes         = a.Notes,
        IsDefault     = a.IsDefault,
        CreatedAt     = a.CreatedAt
    };
}
