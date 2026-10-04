using System.Net.Http.Json;
using SewaEverything.Contracts;

namespace SewaEverything.Client;

public sealed class SessionClient(HttpClient http)
{
    public async Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.PostAsJsonAsync("auth/refresh",
                new RefreshTokenRequest { RefreshToken = refreshToken }, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthResponse>(ct)
            : null;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        try
        {
            await http.PostAsJsonAsync("auth/logout",
                new RefreshTokenRequest { RefreshToken = refreshToken }, ct);
        }
        catch (HttpRequestException)
        {
        }
    }
}
