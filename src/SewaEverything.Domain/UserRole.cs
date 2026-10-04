namespace SewaEverything.Domain;

public enum UserRole
{
    Owner,
    Admin,
    Seller,
    Renter
}

public static class Roles
{
    public const string Owner  = "owner";
    public const string Admin  = "admin";
    public const string Seller = "seller";
    public const string Renter = "renter";

    public const string SelfServiceRegistration = $"{Seller},{Renter}";

    public const string AdminOrOwner = $"{Admin},{Owner}";

    public const string StaffOrSeller = $"{Admin},{Owner},{Seller}";

    public static bool IsStaff(this UserRole role) => role is UserRole.Owner or UserRole.Admin;

    public static string ToDbValue(this UserRole role) => role switch
    {
        UserRole.Owner  => Owner,
        UserRole.Admin  => Admin,
        UserRole.Seller => Seller,
        UserRole.Renter => Renter,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "role tidak dikenal")
    };

    public static UserRole FromDbValue(string value) => value switch
    {
        Owner  => UserRole.Owner,
        Admin  => UserRole.Admin,
        Seller => UserRole.Seller,
        Renter => UserRole.Renter,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "role tidak dikenal")
    };

    public static UserRole? TryFromDbValue(string? value) => value switch
    {
        Owner  => UserRole.Owner,
        Admin  => UserRole.Admin,
        Seller => UserRole.Seller,
        Renter => UserRole.Renter,
        _ => null
    };
}
