using SewaEverything.Client;

namespace SewaEverything.Tests;

public class ClientFormatTests
{
    private static readonly TimeSpan Wib = TimeSpan.FromHours(7);

    private static DateTime WibUtc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, Wib).UtcDateTime;

    [Fact]
    public void Rentang_harian_menampilkan_hari_terakhir_yang_dipakai()
    {
        Assert.Equal("17 Agu 2026 – 19 Agu 2026",
            Format.Range(WibUtc(2026, 8, 17), WibUtc(2026, 8, 20)));
    }

    [Fact]
    public void Sewa_sehari_ditulis_satu_tanggal()
    {
        Assert.Equal("17 Agu 2026", Format.Range(WibUtc(2026, 8, 17), WibUtc(2026, 8, 18)));
    }

    [Fact]
    public void Rentang_berjam_menampilkan_jamnya()
    {
        Assert.Equal("17 Agu 2026 09:00 – 17 Agu 2026 12:00",
            Format.Range(WibUtc(2026, 8, 17, 9), WibUtc(2026, 8, 17, 12)));
    }

    [Fact]
    public void Rentang_lintas_bulan()
    {
        Assert.Equal("30 Agu 2026 – 1 Sep 2026",
            Format.Range(WibUtc(2026, 8, 30), WibUtc(2026, 9, 2)));
    }

    [Fact]
    public void Rentang_kosong_tidak_dimundurkan()
    {
        var sama = WibUtc(2026, 8, 17);
        Assert.Equal("17 Agu 2026 00:00 – 17 Agu 2026 00:00", Format.Range(sama, sama));
    }

    [Fact]
    public void Nominal_negatif_memakai_minus_di_depan_rupiah()
    {
        Assert.Equal("−Rp15.000", Format.Rupiah(-15_000m));
        Assert.Equal("Rp15.000", Format.Rupiah(15_000m));
        Assert.Equal("Rp0", Format.Rupiah(0m));
    }

    [Fact]
    public void Penilaian_kosong_bukan_nol()
    {
        Assert.Equal("Belum ada penilaian", Format.Penilaian(null, 0));
        Assert.Equal("4,5 · 12 penilaian", Format.Penilaian(4.5, 12));
    }
}
