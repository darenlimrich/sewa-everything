namespace SewaEverything.Infrastructure.Storage;

public enum ImageFormat
{
    Jpeg,
    Png,
    WebP
}

public static class ImageSniffer
{
    public const int HeaderLength = 12;

    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> RiffMagic => "RIFF"u8;

    private static ReadOnlySpan<byte> WebPMagic => "WEBP"u8;

    public static bool TryDetect(ReadOnlySpan<byte> header, out ImageFormat format)
    {
        if (header.StartsWith(JpegMagic))
        {
            format = ImageFormat.Jpeg;
            return true;
        }

        if (header.StartsWith(PngMagic))
        {
            format = ImageFormat.Png;
            return true;
        }

        if (header.Length >= HeaderLength
            && header.StartsWith(RiffMagic)
            && header[8..12].SequenceEqual(WebPMagic))
        {
            format = ImageFormat.WebP;
            return true;
        }

        format = default;
        return false;
    }

    public static string Extension(this ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "jpg",
        ImageFormat.Png  => "png",
        ImageFormat.WebP => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "format gambar tidak dikenal")
    };

    public static string ContentType(this ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png  => "image/png",
        ImageFormat.WebP => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "format gambar tidak dikenal")
    };
}
