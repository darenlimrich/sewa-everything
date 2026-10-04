using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SewaEverything.Client;
using SewaEverything.Web;
using SewaEverything.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5288";

builder.Services.AddScoped<TokenProvider>();
builder.Services.AddScoped<ISessionStore, BrowserStorage>();

builder.Services.AddScoped(_ => new SessionClient(new HttpClient { BaseAddress = new Uri(apiBaseUrl) }));
builder.Services.AddScoped<AuthState>();

builder.Services.AddScoped(sp => new HttpClient(
    new AuthenticatingHandler(
        sp.GetRequiredService<TokenProvider>(),
        sp.GetRequiredService<AuthState>()) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(apiBaseUrl)
});
builder.Services.AddScoped<SewaApi>();
builder.Services.AddScoped<CartState>();
builder.Services.AddScoped<IPageVisibility, PageVisibility>();
builder.Services.AddScoped<LiveState>();

await builder.Build().RunAsync();
