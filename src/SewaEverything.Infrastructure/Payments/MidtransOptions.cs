using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SewaEverything.Infrastructure.Persistence;

using SewaEverything.Infrastructure.Security;

namespace SewaEverything.Infrastructure.Payments;

public sealed class MidtransOptions
{
    public const string SectionName = "Midtrans";

    public string ServerKey { get; set; } = string.Empty;

    public string ClientKey { get; set; } = string.Empty;

    public bool IsProduction { get; set; }

    public string NotificationUrl { get; set; } = string.Empty;

    public string QrisAcquirer { get; set; } = "gopay";

    public string BaseUrl => UrlFor(IsProduction);

    public static string UrlFor(bool isProduction) => isProduction
        ? "https://api.midtrans.com"
        : "https://api.sandbox.midtrans.com";
}

public interface IMidtransCredentials
{
    ValueTask<MidtransOptions> CurrentAsync(CancellationToken ct = default);
}

public sealed class ConfiguredMidtransCredentials(IOptionsMonitor<MidtransOptions> options)
    : IMidtransCredentials
{
    public ValueTask<MidtransOptions> CurrentAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(options.CurrentValue);
}

public sealed class PlatformMidtransCredentials(
    SewaDbContext db,
    IOptionsMonitor<MidtransOptions> fallback,
    ISecretProtector protector) : IMidtransCredentials
{
    public async ValueTask<MidtransOptions> CurrentAsync(CancellationToken ct = default)
    {
        var stored = await db.PlatformSettings.AsNoTracking()
            .Select(s => new
            {
                s.MidtransServerKey,
                s.MidtransClientKey,
                s.MidtransIsProduction
            })
            .FirstOrDefaultAsync(ct);

        if (stored is null || string.IsNullOrWhiteSpace(stored.MidtransServerKey))
        {
            return fallback.CurrentValue;
        }

        return new MidtransOptions
        {
            ServerKey       = protector.Reveal(stored.MidtransServerKey) ?? string.Empty,
            ClientKey       = stored.MidtransClientKey ?? string.Empty,
            IsProduction    = stored.MidtransIsProduction,
            NotificationUrl = fallback.CurrentValue.NotificationUrl,
            QrisAcquirer    = fallback.CurrentValue.QrisAcquirer
        };
    }
}
