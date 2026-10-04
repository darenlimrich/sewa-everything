using Microsoft.JSInterop;
using SewaEverything.Client;

namespace SewaEverything.Web.Services;

public sealed class PageVisibility(IJSRuntime js) : IPageVisibility
{
    public async ValueTask<bool> IsVisibleAsync()
    {
        try
        {
            return await js.InvokeAsync<bool>("sewaTerlihat");
        }
        catch (JSException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
