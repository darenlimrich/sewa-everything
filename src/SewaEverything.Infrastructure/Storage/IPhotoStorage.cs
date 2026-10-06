namespace SewaEverything.Infrastructure.Storage;

public interface IPhotoStorage
{
    Task<string> SaveAsync(Stream content, ImageFormat format, CancellationToken ct = default);

    Task DeleteAsync(string url, CancellationToken ct = default);

    Task<StoredPhoto?> ReadAsync(string url, CancellationToken ct = default);
}

public sealed record StoredPhoto(byte[] Content, string ContentType);
