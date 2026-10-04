using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Storage;

public sealed partial class LocalDiskPhotoStorage : IPhotoStorage
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
        var fileName = $"{Guid.NewGuid():N}.{format.Extension()}";

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
        var fileName = url.Split('/')[^1];

        if (!SafeFileName().IsMatch(fileName))
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

    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$")]
    private static partial Regex SafeFileName();
}
