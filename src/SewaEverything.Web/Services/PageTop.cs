using Microsoft.JSInterop;

namespace SewaEverything.Web.Services;

public static class PageTop
{
    public static async Task ScrollAsync(IJSRuntime js)
    {
        try
        {
            await js.InvokeVoidAsync("sewaScrollToPageTop");
        }
        catch (JSException)
        {
        }
    }
}
