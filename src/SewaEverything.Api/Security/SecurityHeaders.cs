namespace SewaEverything.Api.Security;

public static class SecurityHeaders
{
    private const string Permissions =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), " +
        "microphone=(), payment=(), usb=()";

    private const string Policy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public static IApplicationBuilder UseSewaSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var headers = ctx.Response.Headers;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions       = "DENY";
            headers["Referrer-Policy"]  = "no-referrer";
            headers["Permissions-Policy"] = Permissions;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = Policy;

            await next();
        });
}
