using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Storage;

public sealed class LocalDiskPhotoStorage : IPhotoStorage
{
    private readonly PhotoStorageOptions _options;
    private readonly string _rootPath;

    public LocalDiskPhotoStorage(IOptions<PhotoStorageOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;

        _rootPath = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(environment.ContentRootPath, _options.RootPath);

        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public string RequestPath => _options.RequestPath;

    public async Task<string> SaveAsync(Stream content, ImageFormat format, CancellationToken ct = default)
    {
        var fileName = PhotoFileName.New(format);

        var fullPath = Path.Combine(_rootPath, fileName);

        await using (var file = new FileStream(
            fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024,
            useAsync: true))
        {
            await content.CopyToAsync(file, ct);
        }

        return $"{_options.RequestPath}/{fileName}";
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        if (!PhotoFileName.TryFromUrl(url, out var fileName, out _))
        {
            return Task.CompletedTask;
        }

        var fullPath = Path.Combine(_rootPath, fileName);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public async Task<StoredPhoto?> ReadAsync(string url, CancellationToken ct = default)
    {
        if (!PhotoFileName.TryFromUrl(url, out var fileName, out var contentType))
        {
            return null;
        }

        var fullPath = Path.Combine(_rootPath, fileName);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return new StoredPhoto(await File.ReadAllBytesAsync(fullPath, ct), contentType);
    }
}
