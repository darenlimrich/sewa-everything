using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace SewaEverything.Infrastructure.Storage;

public sealed class DatabasePhotoStorage(
    PhotoDataSource dataSource, IOptions<PhotoStorageOptions> options) : IPhotoStorage
{
    private readonly PhotoStorageOptions _options = options.Value;

    public async Task<string> SaveAsync(Stream content, ImageFormat format, CancellationToken ct = default)
    {
        var fileName = PhotoFileName.New(format);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        await using var cmd = dataSource.Value.CreateCommand(
            "INSERT INTO photo_blobs (name, content_type, content) VALUES ($1, $2, $3)");
        cmd.Parameters.Add(new NpgsqlParameter { Value = fileName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = format.ContentType() });
        cmd.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bytea,
            Value = buffer.ToArray()
        });
        await cmd.ExecuteNonQueryAsync(ct);

        return $"{_options.RequestPath}/{fileName}";
    }

    public async Task DeleteAsync(string url, CancellationToken ct = default)
    {
        if (!PhotoFileName.TryFromUrl(url, out var fileName, out _))
        {
            return;
        }

        await using var cmd = dataSource.Value.CreateCommand(
            "DELETE FROM photo_blobs WHERE name = $1");
        cmd.Parameters.Add(new NpgsqlParameter { Value = fileName });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<StoredPhoto?> ReadAsync(string url, CancellationToken ct = default)
    {
        if (!PhotoFileName.TryFromUrl(url, out var fileName, out _))
        {
            return null;
        }

        await using var cmd = dataSource.Value.CreateCommand(
            "SELECT content, content_type FROM photo_blobs WHERE name = $1");
        cmd.Parameters.Add(new NpgsqlParameter { Value = fileName });

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new StoredPhoto(
            await reader.GetFieldValueAsync<byte[]>(0, ct),
            reader.GetString(1));
    }
}

public sealed class PhotoDataSource(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Value { get; } = NpgsqlDataSource.Create(connectionString);

    public ValueTask DisposeAsync() => Value.DisposeAsync();
}
