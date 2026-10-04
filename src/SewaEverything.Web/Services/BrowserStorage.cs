using Microsoft.JSInterop;
using SewaEverything.Client;

namespace SewaEverything.Web.Services;

public sealed class BrowserStorage(IJSRuntime js) : ISessionStore
{
    public Task<string?> GetAsync(string key) =>
        js.InvokeAsync<string?>("localStorage.getItem", key).AsTask();

    public Task SetAsync(string key, string value) =>
        js.InvokeVoidAsync("localStorage.setItem", key, value).AsTask();

    public Task RemoveAsync(string key) =>
        js.InvokeVoidAsync("localStorage.removeItem", key).AsTask();
}
