using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Auth;

public static class OwnerSeeder
{
    public static async Task SeedOwnerAsync(
        this IServiceProvider services,
        Func<string, string?, string?, string?>? periksaSandi = null,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(OwnerSeeder));

        var email    = config["Seed:Owner:Email"];
        var password = config["Seed:Owner:Password"];
        var name     = config["Seed:Owner:Name"] ?? "Owner";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "Seed:Owner tidak dikonfigurasi — pembuatan akun Owner dilewati.");
            return;
        }

        var db = sp.GetRequiredService<SewaDbContext>();

        if (await db.Users.AnyAsync(u => u.Role == UserRole.Owner, ct))
        {
            logger.LogInformation("Akun Owner sudah ada — seeding dilewati.");
            return;
        }

        if (periksaSandi?.Invoke(password, email, name) is { } lemah)
        {
            throw new InvalidOperationException(
                $"Seed:Owner:Password ditolak kebijakan kata sandi: {lemah} " +
                "Akun Owner adalah kunci terakhir instalasi ini — perbaiki konfigurasinya dulu.");
        }

        var hasher = sp.GetRequiredService<IPasswordHasher>();

        db.Users.Add(new User
        {
            Role         = UserRole.Owner,
            Name         = name,
            Email        = email.Trim().ToLowerInvariant(),
            PasswordHash = hasher.Hash(password)
        });

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Akun Owner dibuat untuk {Email} dari konfigurasi Seed:Owner. " +
            "Ganti kata sandinya dan hapus konfigurasi seed setelah login pertama.", email);
    }
}
