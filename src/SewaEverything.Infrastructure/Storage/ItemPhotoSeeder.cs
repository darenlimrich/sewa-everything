using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Storage;

public static class ItemPhotoSeeder
{
    private static readonly (string Title, string FileName)[] Catalog =
    [
        ("Kamera Mirrorless Sony A7 III",               "sony-a7-iii.jpg"),
        ("Kamera Nikon Z6 II + 24-70mm f/4",             "nikon-z6-ii.jpg"),
        ("Lensa Sigma 35mm f/1.4 Art",                  "sigma-35mm-art.jpg"),
        ("Gimbal DJI RS 3 Pro",                         "dji-rs3-pro.jpg"),
        ("Kamera Sony ZV-E10 + 16-50mm",                "sony-zv-e10.jpg"),
        ("Kamera GoPro Hero 12 Black",                  "gopro-hero12.jpg"),
        ("Kamera Sony A7 III + 28-70mm",                "sony-a7-iii-2870.jpg"),
        ("Tripod Manfrotto Befree Advanced",            "manfrotto-befree.jpg"),
        ("Kamera Fujifilm X-T4 + lensa kit 18-55mm",    "fujifilm-xt4.jpg"),
        ("Kamera Canon EOS R6 Mark II + RF 24-105mm",   "canon-r6-m2.jpg"),
        ("DJI Mavic 3 Pro Fly More Combo",              "dji-mavic-3-pro.jpg"),
        ("Sound System Portable 500W + 2 Mic Wireless", "sound-system-500w.jpg"),
    ];

    public static async Task SeedItemPhotosAsync(
        this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var env    = sp.GetRequiredService<IHostEnvironment>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ItemPhotoSeeder));
        var seedDir = Path.Combine(env.ContentRootPath, "SeedData", "Photos");

        if (!Directory.Exists(seedDir))
        {
            logger.LogInformation(
                "Folder SeedData/Photos tidak ada di {Path} — seed foto produk dilewati.",
                seedDir);
            return;
        }

        var db      = sp.GetRequiredService<SewaDbContext>();
        var storage = sp.GetRequiredService<IPhotoStorage>();

        var titles = Catalog.Select(c => c.Title).ToArray();
        var items = await db.Items
            .Where(i => titles.Contains(i.Title))
            .Include(i => i.Photos)
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            logger.LogInformation(
                "Tidak ada listing yang judulnya cocok katalog seed — seed foto dilewati.");
            return;
        }

        var byTitle = items.ToLookup(i => i.Title, StringComparer.Ordinal);
        var applied = 0;
        var skipped = 0;

        foreach (var (title, fileName) in Catalog)
        {
            var seedPath = Path.Combine(seedDir, fileName);
            if (!File.Exists(seedPath))
            {
                logger.LogWarning("Berkas seed foto hilang: {File}", seedPath);
                continue;
            }

            var seedBytes = await File.ReadAllBytesAsync(seedPath, ct);
            if (seedBytes.Length == 0)
            {
                logger.LogWarning("Berkas seed foto kosong: {File}", seedPath);
                continue;
            }

            if (!ImageSniffer.TryDetect(seedBytes, out var format))
            {
                logger.LogWarning(
                    "Berkas seed foto bukan JPEG/PNG/WebP: {File}", seedPath);
                continue;
            }

            var seedHash = SHA256.HashData(seedBytes);

            foreach (var item in byTitle[title])
            {
                if (await AlreadySeededAsync(item, storage, seedHash, ct))
                {
                    skipped++;
                    continue;
                }

                foreach (var old in item.Photos.ToList())
                {
                    db.ItemPhotos.Remove(old);
                    await storage.DeleteAsync(old.Url, ct);
                }

                await using var stream = new MemoryStream(seedBytes, writable: false);
                var url = await storage.SaveAsync(stream, format, ct);

                db.ItemPhotos.Add(new ItemPhoto
                {
                    ItemId    = item.Id,
                    Url       = url,
                    SortOrder = 0
                });

                applied++;
                logger.LogInformation(
                    "Foto seed dipasang: {Title} ← {File}", title, fileName);
            }
        }

        if (applied > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Seed foto produk selesai: {Applied} diganti, {Skipped} sudah cocok.",
            applied, skipped);
    }

    private static async Task<bool> AlreadySeededAsync(
        Item item, IPhotoStorage storage, byte[] seedHash, CancellationToken ct)
    {
        if (item.Photos.Count != 1)
        {
            return false;
        }

        var url = item.Photos.OrderBy(p => p.SortOrder).First().Url;
        var existing = await storage.ReadAsync(url, ct);

        return existing is not null
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing.Content), seedHash);
    }
}
