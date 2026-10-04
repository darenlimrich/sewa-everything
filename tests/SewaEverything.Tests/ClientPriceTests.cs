using SewaEverything.Client;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

public class ClientPriceTests
{
    public static TheoryData<string, PriceUnit, int> Satuan => new()
    {
        { PriceUnits.Day,   PriceUnit.Day,   1 },
        { PriceUnits.Day,   PriceUnit.Day,   6 },
        { PriceUnits.Day,   PriceUnit.Day,   30 },
        { PriceUnits.Week,  PriceUnit.Week,  1 },
        { PriceUnits.Week,  PriceUnit.Week,  7 },
        { PriceUnits.Week,  PriceUnit.Week,  8 },
        { PriceUnits.Week,  PriceUnit.Week,  21 },
        { PriceUnits.Month, PriceUnit.Month, 1 },
        { PriceUnits.Month, PriceUnit.Month, 30 },
        { PriceUnits.Month, PriceUnit.Month, 31 },
        { PriceUnits.Month, PriceUnit.Month, 90 }
    };

    [Theory]
    [MemberData(nameof(Satuan))]
    public void Jumlah_satuan_di_klien_sama_dengan_hitungan_server(string dbUnit, PriceUnit unit, int hari)
    {
        var mulai = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var built = BookingRange.ForDays(mulai, mulai.AddDays(hari - 1));

        Assert.True(built.IsValid, built.Error);

        Assert.Equal(
            BookingCalculator.DurationUnits(unit, built.Start.UtcDateTime, built.End.UtcDateTime),
            BookingRange.Units(dbUnit, built));
    }

    [Fact]
    public void Sewa_yang_dimulai_hari_ini_tetap_dihitung_penuh()
    {
        var hariIni = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(BookingRange.Wib).DateTime);
        var built = BookingRange.ForDays(hariIni, hariIni.AddDays(5));

        Assert.True(built.IsValid, built.Error);
        Assert.Equal(6, BookingRange.Units(PriceUnits.Day, built));

        Assert.Equal(
            BookingCalculator.DurationUnits(PriceUnit.Day, built.Start.UtcDateTime, built.End.UtcDateTime),
            BookingRange.Units(PriceUnits.Day, built));
    }

    [Fact]
    public void Rentang_berjam_dihitung_dari_jamnya()
    {
        var besok = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var built = BookingRange.ForHours(besok, 9, 12);

        Assert.True(built.IsValid, built.Error);
        Assert.Equal(3, BookingRange.Units(PriceUnits.Hour, built));

        Assert.Equal(
            BookingCalculator.DurationUnits(PriceUnit.Hour, built.Start.UtcDateTime, built.End.UtcDateTime),
            BookingRange.Units(PriceUnits.Hour, built));
    }

    [Fact]
    public void Satuan_tak_dikenal_tidak_menghasilkan_tebakan()
    {
        var mulai = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var built = BookingRange.ForDays(mulai, mulai.AddDays(5));

        Assert.Equal(0, BookingRange.Units("tahun", built));
        Assert.Null(BookingRange.UnitLength("tahun"));
    }

    [Fact]
    public void Rentang_tidak_sah_tidak_menghasilkan_satuan()
    {
        var lewat = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);

        Assert.Equal(0, BookingRange.Units(PriceUnits.Day, BookingRange.ForDays(lewat, lewat)));
    }

    [Fact]
    public void Satuan_jamak_menyebut_jumlahnya()
    {
        Assert.Equal("/hari", Format.PriceUnit(PriceUnits.Day, 0));
        Assert.Equal("/hari", Format.PriceUnit(PriceUnits.Day, 1));
        Assert.Equal("/6 hari", Format.PriceUnit(PriceUnits.Day, 6));
        Assert.Equal("/3 jam", Format.PriceUnit(PriceUnits.Hour, 3));
        Assert.Equal("/2 minggu", Format.PriceUnit(PriceUnits.Week, 2));
        Assert.Equal("/2 bulan", Format.PriceUnit(PriceUnits.Month, 2));
    }
}
