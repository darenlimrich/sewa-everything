using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SewaEverything.Api.Security;

public static class RateLimiting
{
    public static IServiceCollection AddSewaRateLimiter(
        this IServiceCollection services, SecurityOptions security)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var jeda))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)jeda.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                ctx.HttpContext.Response.ContentType = "application/problem+json";

                await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title  = "Terlalu banyak permintaan",
                    Detail = "Permintaan dari jaringan ini terlalu sering. " +
                             "Tunggu sebentar, lalu coba lagi."
                }, ct);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"global:{Kunci(ctx)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = security.GlobalPerMinute,
                        Window      = TimeSpan.FromMinutes(1)
                    }));

            Tetap(options, SewaRateLimits.Login, security.LoginPerWindow,
                TimeSpan.FromMinutes(security.LoginWindowMinutes));

            Tetap(options, SewaRateLimits.Register, security.RegisterPerHour, TimeSpan.FromHours(1));

            Tetap(options, SewaRateLimits.Refresh, security.RefreshPerMinute, TimeSpan.FromMinutes(1));

            Tetap(options, SewaRateLimits.ForgotPassword, security.ForgotPasswordPerHour,
                TimeSpan.FromHours(1));

            Tetap(options, SewaRateLimits.ResetPassword, security.ResetPasswordPerHour,
                TimeSpan.FromHours(1));

            Tetap(options, SewaRateLimits.Webhook, security.WebhookPerMinute, TimeSpan.FromMinutes(1));
        });

        return services;
    }

    private static void Tetap(
        RateLimiterOptions options, string nama, int batas, TimeSpan jendela) =>
        options.AddPolicy(nama, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                $"{nama}:{Kunci(ctx)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = batas,
                    Window      = jendela
                }));

    private static string Kunci(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "tanpa-ip";
}

public static class SewaRateLimits
{
    public const string Login          = "login";
    public const string Register       = "register";
    public const string Refresh        = "refresh";
    public const string ForgotPassword = "forgot-password";
    public const string ResetPassword  = "reset-password";
    public const string Webhook        = "webhook";
}
