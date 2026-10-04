using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Bookings;

public sealed class ReturnDueSweeperOptions
{
    public const string SectionName = "Bookings:ReturnDueSweeper";

    [Range(1, 86_400, ErrorMessage = "Jeda sapuan pengembalian harus 1–86400 detik.")]
    public int IntervalSeconds { get; set; } = 600;

    public bool Enabled { get; set; } = true;
}

public sealed class ReturnDueSweeperService(
    IServiceScopeFactory scopes,
    IOptions<ReturnDueSweeperOptions> options,
    ILogger<ReturnDueSweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Job penyelesai pengembalian dimatikan lewat konfigurasi.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<ReturnDueSweeper>();

                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sapuan pengembalian gagal; akan dicoba lagi pada jeda berikutnya.");
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
