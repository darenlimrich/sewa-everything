namespace SewaEverything.Api.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public bool RateLimiting { get; set; } = true;

    public bool RequireHttps { get; set; } = true;

    public bool TrustProxyHeaders { get; set; }

    public string[] KnownProxies { get; set; } = [];

    public bool TrustAllProxies { get; set; }

    public int LoginPerWindow { get; set; } = 10;

    public int LoginWindowMinutes { get; set; } = 5;

    public int RegisterPerHour { get; set; } = 5;

    public int RefreshPerMinute { get; set; } = 60;

    public int ForgotPasswordPerHour { get; set; } = 5;

    public int ResetPasswordPerHour { get; set; } = 20;

    public int WebhookPerMinute { get; set; } = 600;

    public int GlobalPerMinute { get; set; } = 300;
}
