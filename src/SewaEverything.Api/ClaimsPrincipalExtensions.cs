using System.Security.Claims;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Auth;

namespace SewaEverything.Api;

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(SewaClaims.Subject);

        return Guid.TryParse(raw, out var id)
            ? id
            : throw new InvalidOperationException(
                $"Claim '{SewaClaims.Subject}' tidak ada atau bukan GUID yang sah.");
    }

    public static UserRole Role(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(SewaClaims.Role);

        return raw is null
            ? throw new InvalidOperationException($"Claim '{SewaClaims.Role}' tidak ada.")
            : Roles.FromDbValue(raw);
    }

    public static Guid? UserIdOrNull(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SewaClaims.Subject), out var id) ? id : null;

    public static UserRole? RoleOrNull(this ClaimsPrincipal principal) =>
        Roles.TryFromDbValue(principal.FindFirstValue(SewaClaims.Role));
}
