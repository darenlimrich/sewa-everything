using System.Buffers.Binary;

namespace SewaEverything.Infrastructure.Storage;

public readonly record struct SanitizeResult(byte[]? Bytes, string? Error)
{
    public bool Ok => Bytes is not null;

    public static SanitizeResult Fail(string error) => new(null, error);

    public static SanitizeResult Success(byte[] bytes) => new(bytes, null);
}

public static class ImageSanitizer
{
    private const string Rusak =
        "Berkas ini mengaku gambar tapi isinya tidak utuh. Unggah ulang berkas aslinya.";

    public static SanitizeResult Sanitize(ReadOnlySpan<byte> source, ImageFormat format) =>
        format switch
        {
            ImageFormat.Jpeg => Jpeg(source),
            ImageFormat.Png  => Png(source),
            ImageFormat.WebP => WebP(source),
            _ => SanitizeResult.Fail(Rusak)
        };

    private static SanitizeResult Jpeg(ReadOnlySpan<byte> source)
    {
        if (source.Length < 4 || source[0] != 0xFF || source[1] != 0xD8)
        {
            return SanitizeResult.Fail(Rusak);
        }

        var keluar = new MemoryStream(source.Length);
        keluar.Write([0xFF, 0xD8]);

        var i = 2;

        while (i + 1 < source.Length)
        {
            if (source[i] != 0xFF)
            {
                return SanitizeResult.Fail(Rusak);
            }

            var marker = source[i + 1];

            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker == 0xD9)
            {
                keluar.Write([0xFF, 0xD9]);
                return SanitizeResult.Success(keluar.ToArray());
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                keluar.Write([0xFF, marker]);
                i += 2;
                continue;
            }

            if (i + 3 >= source.Length)
            {
                return SanitizeResult.Fail(Rusak);
            }

            var panjang = BinaryPrimitives.ReadUInt16BigEndian(source[(i + 2)..]);

            if (panjang < 2 || i + 2 + panjang > source.Length)
            {
                return SanitizeResult.Fail(Rusak);
            }

            var buang = marker is >= 0xE1 and <= 0xEF || marker == 0xFE;

            if (!buang)
            {
                keluar.Write([0xFF, marker]);
                keluar.Write(source.Slice(i + 2, panjang));
            }

            i += 2 + panjang;

            if (marker != 0xDA)
            {
                continue;
            }

            var sisa = source[i..];
            var akhir = sisa.LastIndexOf<byte>([0xFF, 0xD9]);

            if (akhir < 0)
            {
                return SanitizeResult.Fail(Rusak);
            }

            keluar.Write(sisa[..(akhir + 2)]);

            return SanitizeResult.Success(keluar.ToArray());
        }

        return SanitizeResult.Fail(Rusak);
    }

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly string[] PngDipertahankan =
        ["IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP",
         "acTL", "fcTL", "fdAT", "sBIT", "bKGD"];

    private static SanitizeResult Png(ReadOnlySpan<byte> source)
    {
        if (source.Length < 8 || !source[..8].SequenceEqual(PngSignature))
        {
            return SanitizeResult.Fail(Rusak);
        }

        var keluar = new MemoryStream(source.Length);
        keluar.Write(PngSignature);

        var i = 8;
        var lihatIhdr = false;

        while (i + 12 <= source.Length)
        {
            var panjang = BinaryPrimitives.ReadUInt32BigEndian(source[i..]);

            if (panjang > int.MaxValue - 12 || i + 12 + (int)panjang > source.Length)
            {
                return SanitizeResult.Fail(Rusak);
            }

            var jenis = System.Text.Encoding.ASCII.GetString(source.Slice(i + 4, 4));
            var utuh = source.Slice(i, 12 + (int)panjang);

            if (jenis == "IHDR")
            {
                lihatIhdr = true;
            }

            if (PngDipertahankan.Contains(jenis))
            {
                keluar.Write(utuh);
            }

            i += 12 + (int)panjang;

            if (jenis == "IEND")
            {
                return lihatIhdr
                    ? SanitizeResult.Success(keluar.ToArray())
                    : SanitizeResult.Fail(Rusak);
            }
        }

        return SanitizeResult.Fail(Rusak);
    }

    private static SanitizeResult WebP(ReadOnlySpan<byte> source)
    {
        if (source.Length < 12
            || !source[..4].SequenceEqual("RIFF"u8)
            || !source.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return SanitizeResult.Fail(Rusak);
        }

        var deklarasi = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);

        if (deklarasi > int.MaxValue - 8 || 8 + (int)deklarasi > source.Length)
        {
            return SanitizeResult.Fail(Rusak);
        }

        var isi = source.Slice(12, (int)deklarasi - 4);
        var keluar = new MemoryStream(source.Length);

        var i = 0;
        var adaGambar = false;

        while (i + 8 <= isi.Length)
        {
            var jenis = System.Text.Encoding.ASCII.GetString(isi.Slice(i, 4));
            var panjang = BinaryPrimitives.ReadUInt32LittleEndian(isi[(i + 4)..]);

            if (panjang > int.MaxValue - 9)
            {
                return SanitizeResult.Fail(Rusak);
            }

            var berbantalan = (int)panjang + ((int)panjang & 1);

            if (i + 8 + berbantalan > isi.Length)
            {
                return SanitizeResult.Fail(Rusak);
            }

            if (jenis is "VP8 " or "VP8L" or "VP8X" or "ALPH" or "ANIM" or "ANMF")
            {
                if (jenis is "VP8 " or "VP8L" or "ANMF")
                {
                    adaGambar = true;
                }

                var potong = isi.Slice(i, 8 + berbantalan).ToArray();

                if (jenis == "VP8X" && potong.Length >= 9)
                {
                    potong[8] &= 0b1111_0011;
                }

                keluar.Write(potong);
            }

            i += 8 + berbantalan;
        }

        if (!adaGambar)
        {
            return SanitizeResult.Fail(Rusak);
        }

        var badan = keluar.ToArray();
        var hasil = new byte[12 + badan.Length];

        "RIFF"u8.CopyTo(hasil);
        BinaryPrimitives.WriteUInt32LittleEndian(hasil.AsSpan(4), (uint)(badan.Length + 4));
        "WEBP"u8.CopyTo(hasil.AsSpan(8));
        badan.CopyTo(hasil, 12);

        return SanitizeResult.Success(hasil);
    }
}
