using System.Text.Json;
using SewaEverything.Contracts;

namespace SewaEverything.Client;

public sealed class AuthState(ISessionStore storage, TokenProvider tokens, SessionClient session)
{
    private const string StorageKey = "se.auth";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private Task? _init;
    private Task<bool>? _refreshing;
    private AuthResponse? _session;

    public UserResponse? User => _session?.User;
    public bool IsAuthenticated => _session is not null;

    public bool NeedsRefresh => _session is not null && Expiring(_session.ExpiresAt, RefreshMargin);

    public event Action? Changed;

    public Task InitializeAsync() => _init ??= LoadAsync();

    private async Task LoadAsync()
    {
        var raw = await storage.GetAsync(StorageKey);
        if (string.IsNullOrEmpty(raw)) return;

        AuthResponse? saved;

        try { saved = JsonSerializer.Deserialize<AuthResponse>(raw, Json); }
        catch (JsonException) { saved = null; }

        if (saved is null || Expiring(saved.RefreshTokenExpiresAt, TimeSpan.FromSeconds(30)))
        {
            await storage.RemoveAsync(StorageKey);
            return;
        }

        Apply(saved);

        if (NeedsRefresh)
        {
            await RefreshAsync();
        }
    }

    public async Task SignInAsync(AuthResponse auth)
    {
        Apply(auth);
        await storage.SetAsync(StorageKey, JsonSerializer.Serialize(auth, Json));
        _init = Task.CompletedTask;
        Changed?.Invoke();
    }

    public Task<bool> RefreshAsync() => _refreshing ??= RunRefreshAsync();

    private async Task<bool> RunRefreshAsync()
    {
        try
        {
            var token = _session?.RefreshToken;

            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var auth = await session.RefreshAsync(token);

            if (auth is null)
            {
                await SignOutLocallyAsync();
                return false;
            }

            await SignInAsync(auth);
            return true;
        }
        finally
        {
            _refreshing = null;
        }
    }

    public async Task UpdateUserAsync(UserResponse user)
    {
        if (_session is null) return;

        _session = _session with { User = user };
        await storage.SetAsync(StorageKey, JsonSerializer.Serialize(_session, Json));
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        var token = _session?.RefreshToken;

        if (!string.IsNullOrEmpty(token))
        {
            await session.LogoutAsync(token);
        }

        await SignOutLocallyAsync();
    }

    private async Task SignOutLocallyAsync()
    {
        _session = null;
        tokens.Token = null;
        await storage.RemoveAsync(StorageKey);
        _init = Task.CompletedTask;
        Changed?.Invoke();
    }

    private void Apply(AuthResponse auth)
    {
        _session = auth;
        tokens.Token = auth.AccessToken;
    }

    private static bool Expiring(DateTime expiresAt, TimeSpan margin)
    {
        var utc = expiresAt.Kind switch
        {
            DateTimeKind.Utc   => expiresAt,
            DateTimeKind.Local => expiresAt.ToUniversalTime(),
            _                  => DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)
        };

        return utc <= DateTime.UtcNow.Add(margin);
    }
}
