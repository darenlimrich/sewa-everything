using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SewaEverything.Infrastructure.Storage;

public static partial class PhotoFileName
{
    public static string New(ImageFormat format) => $"{Guid.NewGuid():N}.{format.Extension()}";

    public static bool TryParse(
        string fileName, [NotNullWhen(true)] out string? contentType)
    {
        var match = Pattern().Match(fileName);

        contentType = match.Success
            ? match.Groups["ext"].Value switch
            {
                "jpg"  => ImageFormat.Jpeg.ContentType(),
                "png"  => ImageFormat.Png.ContentType(),
                _      => ImageFormat.WebP.ContentType()
            }
            : null;

        return contentType is not null;
    }

    public static bool TryFromUrl(
        string url, out string fileName, [NotNullWhen(true)] out string? contentType)
    {
        fileName = url.Split('/')[^1];
        return TryParse(fileName, out contentType);
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.(?<ext>jpg|png|webp)$")]
    private static partial Regex Pattern();
}
