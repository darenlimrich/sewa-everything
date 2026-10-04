using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Security;

public static class SecretMigration
{
    public static async Task ProtectStoredSecretsAsync(
        this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var protector = sp.GetRequiredService<ISecretProtector>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SecretMigration));

        var db = sp.GetRequiredService<SewaDbContext>();
        var settings = await db.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings?.MidtransServerKey is not { Length: > 0 } tersimpan
            || protector.IsProtected(tersimpan))
        {
            return;
        }

        if (!protector.IsConfigured)
        {
            logger.LogWarning(
                "Server key Midtrans tersimpan POLOS di database dan Security:SecretKey belum " +
                "diisi, jadi ia tidak dapat dienkripsi. Isi Security__SecretKey lalu jalankan " +
                "ulang aplikasi.");

            return;
        }

        settings.MidtransServerKey = protector.Protect(tersimpan);
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Server key Midtrans yang tersimpan polos sudah dienkripsi ulang di tempat.");
    }
}
