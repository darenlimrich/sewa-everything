using System.Net.Http.Headers;

namespace SewaEverything.Client;

public sealed class AuthenticatingHandler(TokenProvider tokens, AuthState session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (session.NeedsRefresh)
        {
            await session.RefreshAsync();
        }

        if (!string.IsNullOrEmpty(tokens.Token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
