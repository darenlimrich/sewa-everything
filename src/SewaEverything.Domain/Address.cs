namespace SewaEverything.Domain;

public sealed class Address
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Label { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string FullAddress { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum DeliveryMethod
{
    Pickup,

    Delivery
}

public static class DeliveryMethods
{
    public const string Pickup   = "pickup";
    public const string Delivery = "delivery";

    public static readonly string[] DbValues = [Pickup, Delivery];

    public static string ToDbValue(this DeliveryMethod method) => method switch
    {
        DeliveryMethod.Pickup   => Pickup,
        DeliveryMethod.Delivery => Delivery,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "metode pengiriman tidak dikenal")
    };

    public static DeliveryMethod FromDbValue(string value) => value switch
    {
        Pickup   => DeliveryMethod.Pickup,
        Delivery => DeliveryMethod.Delivery,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "metode pengiriman tidak dikenal")
    };

    public static bool IsKnown(string? value) =>
        value is not null && Array.IndexOf(DbValues, value) >= 0;
}
