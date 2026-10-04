using Microsoft.AspNetCore.Identity;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Auth;

public enum PasswordCheck
{
    Failed,
    Ok,

    OkNeedsRehash
}

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string hash, string password);
}

public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();
    private static readonly User Dummy = new();

    public string Hash(string password) => _inner.HashPassword(Dummy, password);

    public PasswordCheck Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(Dummy, hash, password) switch
        {
            PasswordVerificationResult.Success              => PasswordCheck.Ok,
            PasswordVerificationResult.SuccessRehashNeeded  => PasswordCheck.OkNeedsRehash,
            _                                               => PasswordCheck.Failed
        };
}
