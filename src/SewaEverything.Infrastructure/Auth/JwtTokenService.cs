using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddHours(_options.AccessTokenHours);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer   = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires  = expiresAt,
            Claims = new Dictionary<string, object>
            {
                [SewaClaims.Subject]    = user.Id.ToString(),
                [SewaClaims.Email]      = user.Email,
                [SewaClaims.Name]       = user.Name,
                [SewaClaims.Role]       = user.Role.ToDbValue(),
                [SewaClaims.IsVerified] = user.IsVerified
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
