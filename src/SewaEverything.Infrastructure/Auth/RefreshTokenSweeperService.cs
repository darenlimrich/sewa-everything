using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Auth;

public sealed class RefreshTokenSweeperOptions
{
    public const string SectionName = "Auth:RefreshTokenSweeper";

    [Range(60, 86400, ErrorMessage = "Jeda sapuan refresh token harus 60–86400 detik.")]
    public int IntervalSeconds { get; set; } = 3600;

    [Range(1, 3650, ErrorMessage = "Masa simpan baris mati harus 1–3650 hari.")]
    public int RetentionDays { get; set; } = 30;

    public bool Enabled { get; set; } = true;
}

public sealed class RefreshTokenSweeperService(
    IServiceScopeFactory scopes,
    IOptions<RefreshTokenSweeperOptions> options,
    ILogger<RefreshTokenSweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Job pembuang refresh token mati dimatikan lewat konfigurasi.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<RefreshTokenSweeper>();

                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "Sapuan refresh token gagal; akan dicoba lagi pada jeda berikutnya.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
