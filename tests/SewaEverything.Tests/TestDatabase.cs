using Npgsql;

namespace SewaEverything.Tests;

public static class TestDatabase
{
    public const string Name = "sewa_everything_test";

    private const string Host = "Host=127.0.0.1;Port=5432;Username=sewa;Password=sewa_dev";

    public static string ConnectionString => $"{Host};Database={Name}";

    private static string MaintenanceConnectionString => $"{Host};Database=postgres";

    public static async Task RecreateAsync(CancellationToken ct = default)
    {
        NpgsqlConnection.ClearAllPools();

        await using (var maintenance = new NpgsqlConnection(MaintenanceConnectionString))
        {
            await maintenance.OpenAsync(ct);
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {Name} WITH (FORCE)", ct);
            await ExecuteAsync(maintenance, $"CREATE DATABASE {Name}", ct);
        }

        await using var db = new NpgsqlConnection(ConnectionString);
        await db.OpenAsync(ct);

        foreach (var file in MigrationFiles())
        {
            await ExecuteAsync(db, await File.ReadAllTextAsync(file, ct), ct);
        }
    }

    private static IEnumerable<string> MigrationFiles()
    {
        var directory = Path.GetDirectoryName(FindRepoFile("db", "migrations", "0001_init.sql"))!;

        return Directory.EnumerateFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var relative = Path.Combine(segments);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Tidak menemukan '{relative}' di direktori mana pun di atas {AppContext.BaseDirectory}.");
    }
}
