using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Bookings;

public sealed class HoldSweeperOptions
{
    public const string SectionName = "Bookings:HoldSweeper";

    [Range(1, 3600, ErrorMessage = "Jeda sapuan hold harus 1–3600 detik.")]
    public int IntervalSeconds { get; set; } = 60;

    public bool Enabled { get; set; } = true;
}

public sealed class HoldSweeperService(
    IServiceScopeFactory scopes,
    IOptions<HoldSweeperOptions> options,
    ILogger<HoldSweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Job pelepas hold dimatikan lewat konfigurasi.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<HoldSweeper>();

                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sapuan hold gagal; akan dicoba lagi pada jeda berikutnya.");
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
