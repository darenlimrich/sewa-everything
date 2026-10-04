using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Auth;

public sealed record AccessToken(string Value, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken Create(User user);
}
