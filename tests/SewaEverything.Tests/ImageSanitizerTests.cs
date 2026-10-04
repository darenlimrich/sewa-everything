using System.Buffers.Binary;
using System.Text;
using SewaEverything.Infrastructure.Storage;

namespace SewaEverything.Tests;

public class ImageSanitizerTests
{
    private static readonly byte[] Exif =
        Encoding.ASCII.GetBytes("Exif\0\0MM\0*GPSLatitude=-6.9034 GPSLongitude=107.6181");

    private static byte[] JpegDenganSegmen(byte marker, byte[] muatan)
    {
        var asli = ItemPhotoTests.Jpeg();
        var panjang = muatan.Length + 2;

        return
        [
            asli[0], asli[1],
            0xFF, marker, (byte)(panjang >> 8), (byte)(panjang & 0xFF),
            .. muatan,
            .. asli[2..]
        ];
    }

    private static byte[] PngDenganChunk(string jenis, byte[] muatan)
    {
        var asli = ItemPhotoTests.Png();
        var chunk = new byte[12 + muatan.Length];

        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)muatan.Length);
        Encoding.ASCII.GetBytes(jenis).CopyTo(chunk, 4);
        muatan.CopyTo(chunk, 8);

        return [.. asli[..8], .. chunk, .. asli[8..]];
    }

    [Fact]
    public void Jpeg_yang_sah_lolos_dan_tetap_jpeg()
    {
        var hasil = ImageSanitizer.Sanitize(ItemPhotoTests.Jpeg(), ImageFormat.Jpeg);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.Equal([0xFF, 0xD8], hasil.Bytes![..2]);
        Assert.Equal([0xFF, 0xD9], hasil.Bytes[^2..]);
    }

    [Fact]
    public void Exif_di_app1_dibuang_seluruhnya()
    {
        var kotor = JpegDenganSegmen(0xE1, Exif);

        Assert.Contains("GPSLatitude", Encoding.ASCII.GetString(kotor), StringComparison.Ordinal);

        var hasil = ImageSanitizer.Sanitize(kotor, ImageFormat.Jpeg);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.DoesNotContain("GPSLatitude", Encoding.ASCII.GetString(hasil.Bytes!),
            StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(hasil.Bytes!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Komentar_jpeg_ikut_dibuang()
    {
        var kotor = JpegDenganSegmen(0xFE, Encoding.ASCII.GetBytes("rahasia-di-komentar"));

        var hasil = ImageSanitizer.Sanitize(kotor, ImageFormat.Jpeg);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.DoesNotContain("rahasia-di-komentar", Encoding.ASCII.GetString(hasil.Bytes!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Jpeg_palsu_hanya_bermagic_ditolak()
    {
        byte[] palsu = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[128]];

        Assert.False(ImageSanitizer.Sanitize(palsu, ImageFormat.Jpeg).Ok);
    }

    [Fact]
    public void Png_yang_sah_lolos_tanpa_berubah()
    {
        var asli = ItemPhotoTests.Png();
        var hasil = ImageSanitizer.Sanitize(asli, ImageFormat.Png);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.Equal(asli, hasil.Bytes);
    }

    [Fact]
    public void Chunk_metadata_png_dibuang()
    {
        var kotor = PngDenganChunk("tEXt", Encoding.ASCII.GetBytes("Comment\0lokasi-rumah"));

        Assert.Contains("lokasi-rumah", Encoding.ASCII.GetString(kotor), StringComparison.Ordinal);

        var hasil = ImageSanitizer.Sanitize(kotor, ImageFormat.Png);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.DoesNotContain("lokasi-rumah", Encoding.ASCII.GetString(hasil.Bytes!),
            StringComparison.Ordinal);
        Assert.Equal(ItemPhotoTests.Png(), hasil.Bytes);
    }

    [Fact]
    public void Exif_chunk_png_dibuang()
    {
        var kotor = PngDenganChunk("eXIf", Exif);
        var hasil = ImageSanitizer.Sanitize(kotor, ImageFormat.Png);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.DoesNotContain("GPSLatitude", Encoding.ASCII.GetString(hasil.Bytes!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Png_palsu_hanya_bertanda_tangan_ditolak()
    {
        byte[] palsu =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Encoding.ASCII.GetBytes("<?php echo 1;")];

        Assert.False(ImageSanitizer.Sanitize(palsu, ImageFormat.Png).Ok);
    }

    [Fact]
    public void Png_tanpa_iend_ditolak()
    {
        var asli = ItemPhotoTests.Png();

        Assert.False(ImageSanitizer.Sanitize(asli[..^8], ImageFormat.Png).Ok);
    }

    private static byte[] WebP(params (string Jenis, byte[] Isi)[] chunk)
    {
        var badan = new MemoryStream();

        foreach (var (jenis, isi) in chunk)
        {
            badan.Write(Encoding.ASCII.GetBytes(jenis));

            var panjang = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(panjang, (uint)isi.Length);
            badan.Write(panjang);
            badan.Write(isi);

            if (isi.Length % 2 == 1)
            {
                badan.WriteByte(0);
            }
        }

        var isiBadan = badan.ToArray();
        var hasil = new byte[12 + isiBadan.Length];

        Encoding.ASCII.GetBytes("RIFF").CopyTo(hasil, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(hasil.AsSpan(4), (uint)(isiBadan.Length + 4));
        Encoding.ASCII.GetBytes("WEBP").CopyTo(hasil, 8);
        isiBadan.CopyTo(hasil, 12);

        return hasil;
    }

    [Fact]
    public void Chunk_exif_webp_dibuang_dan_gambarnya_dipertahankan()
    {
        var kotor = WebP(("VP8 ", [1, 2, 3, 4]), ("EXIF", Exif));

        var hasil = ImageSanitizer.Sanitize(kotor, ImageFormat.WebP);

        Assert.True(hasil.Ok, hasil.Error);
        Assert.DoesNotContain("GPSLatitude", Encoding.ASCII.GetString(hasil.Bytes!),
            StringComparison.Ordinal);
        Assert.Contains("VP8 ", Encoding.ASCII.GetString(hasil.Bytes!), StringComparison.Ordinal);
    }

    [Fact]
    public void Webp_tanpa_chunk_gambar_ditolak()
    {
        var kosong = WebP(("EXIF", Exif));

        Assert.False(ImageSanitizer.Sanitize(kosong, ImageFormat.WebP).Ok);
    }

    [Fact]
    public void Flag_metadata_di_vp8x_ikut_dibersihkan()
    {
        byte[] vp8x = [0b0000_1100, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var hasil = ImageSanitizer.Sanitize(
            WebP(("VP8X", vp8x), ("VP8 ", [1, 2, 3, 4]), ("EXIF", Exif)), ImageFormat.WebP);

        Assert.True(hasil.Ok, hasil.Error);

        var isi = hasil.Bytes!;
        var posisi = isi.AsSpan().IndexOf("VP8X"u8);

        Assert.True(posisi >= 0);
        Assert.Equal(0, isi[posisi + 8] & 0b0000_1100);
    }
}
