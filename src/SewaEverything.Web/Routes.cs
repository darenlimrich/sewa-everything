namespace SewaEverything.Web;

public static class Routes
{
    public const string PublicLogin = "/masuk";

    public const string StaffLogin = "/staf";

    public static string HomeFor(string? role) => role switch
    {
        "owner"  => "/owner/pendapatan",
        "admin"  => "/admin",
        "seller" => "/jual",
        _        => "/"
    };

    public static bool IsStaff(string? role) => role is "admin" or "owner";

    public static string? StaffPanel(string? role) => IsStaff(role) ? HomeFor(role) : null;

    public static bool IsLocalPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in url)
        {
            if (c == '\\' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
