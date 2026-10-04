using SewaEverything.Client;
using SewaEverything.Contracts;

namespace SewaEverything.Tests;

public class ClientCalendarTests
{
    private static readonly TimeSpan Wib = TimeSpan.FromHours(7);

    private static DateTime WibMidnightUtc(int year, int month, int day) =>
        new DateTimeOffset(year, month, day, 0, 0, 0, Wib).UtcDateTime;

    private static BlockedRangeResponse Blok(DateTime startUtc, DateTime endUtc) =>
        new() { StartsAt = startUtc, EndsAt = endUtc, Source = "booking" };

    [Fact]
    public void Jendela_kosong_di_tanggal_minimum_tidak_melempar()
    {
        var model = new AvailabilityCalendarModel([], default, default);

        Assert.Equal(default, model.MinDate);
        Assert.False(model.IsBlocked(default));
    }

    [Fact]
    public void Jendela_di_tanggal_maksimum_tidak_melempar()
    {
        var akhir = DateOnly.MaxValue;
        var model = new AvailabilityCalendarModel(
            [Blok(DateTime.MaxValue.AddDays(-2), DateTime.MaxValue)], akhir.AddDays(-3), akhir);

        Assert.Equal(akhir, model.MaxDate);
    }

    [Fact]
    public void Hari_terakhir_rentang_tidak_ikut_terisi()
    {
        var model = new AvailabilityCalendarModel(
            [Blok(WibMidnightUtc(2026, 8, 17), WibMidnightUtc(2026, 8, 20))],
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 16)));
        Assert.True(model.IsBlocked(new DateOnly(2026, 8, 17)));
        Assert.True(model.IsBlocked(new DateOnly(2026, 8, 18)));
        Assert.True(model.IsBlocked(new DateOnly(2026, 8, 19)));
        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 20)));
    }

    [Fact]
    public void Rentang_berjam_menandai_harinya()
    {
        var mulai = WibMidnightUtc(2026, 8, 17).AddHours(9);
        var model = new AvailabilityCalendarModel(
            [Blok(mulai, mulai.AddHours(3))],
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.True(model.IsBlocked(new DateOnly(2026, 8, 17)));
        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 18)));
    }

    [Fact]
    public void Rentang_yang_melewati_tengah_malam_menandai_dua_hari()
    {
        var mulai = WibMidnightUtc(2026, 8, 17).AddHours(20);
        var model = new AvailabilityCalendarModel(
            [Blok(mulai, mulai.AddHours(6))],
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.True(model.IsBlocked(new DateOnly(2026, 8, 17)));
        Assert.True(model.IsBlocked(new DateOnly(2026, 8, 18)));
        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 19)));
    }

    [Fact]
    public void Rentang_di_luar_jendela_diabaikan()
    {
        var model = new AvailabilityCalendarModel(
            [Blok(WibMidnightUtc(2026, 5, 1), WibMidnightUtc(2026, 5, 5))],
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        for (var d = new DateOnly(2026, 8, 1); d <= new DateOnly(2026, 8, 31); d = d.AddDays(1))
        {
            Assert.False(model.IsBlocked(d));
        }
    }

    [Fact]
    public void Pilihan_menolak_hari_di_luar_jendela_dan_hari_terisi()
    {
        var model = new AvailabilityCalendarModel(
            [Blok(WibMidnightUtc(2026, 8, 17), WibMidnightUtc(2026, 8, 18))],
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20));

        Assert.False(model.Pick(new DateOnly(2026, 8, 9)));
        Assert.False(model.Pick(new DateOnly(2026, 8, 21)));
        Assert.False(model.Pick(new DateOnly(2026, 8, 17)));
        Assert.Null(model.Selection);

        Assert.True(model.Pick(new DateOnly(2026, 8, 12)));
        Assert.Equal((new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12)), model.Selection);
        Assert.Equal(1, model.SelectedDayCount);
    }

    [Fact]
    public void Ketukan_kedua_memperpanjang_kecuali_melompati_hari_terisi()
    {
        var model = new AvailabilityCalendarModel(
            [Blok(WibMidnightUtc(2026, 8, 15), WibMidnightUtc(2026, 8, 16))],
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20));

        model.Pick(new DateOnly(2026, 8, 11));
        model.Pick(new DateOnly(2026, 8, 13));
        Assert.Equal((new DateOnly(2026, 8, 11), new DateOnly(2026, 8, 13)), model.Selection);
        Assert.Equal(3, model.SelectedDayCount);

        model.Pick(new DateOnly(2026, 8, 12));
        model.Pick(new DateOnly(2026, 8, 18));
        Assert.Equal((new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 18)), model.Selection);
    }

    [Fact]
    public void Ketukan_mundur_tetap_menghasilkan_rentang_naik()
    {
        var model = new AvailabilityCalendarModel(
            [], new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20));

        model.Pick(new DateOnly(2026, 8, 18));
        model.Pick(new DateOnly(2026, 8, 14));

        Assert.Equal((new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 18)), model.Selection);
    }

    [Fact]
    public void Jendela_terbalik_dijinakkan()
    {
        var model = new AvailabilityCalendarModel(
            [], new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 10));

        Assert.Equal(new DateOnly(2026, 8, 20), model.MinDate);
        Assert.Equal(new DateOnly(2026, 8, 20), model.MaxDate);
    }

    [Fact]
    public void Rentang_kosong_atau_terbalik_diabaikan()
    {
        var model = new AvailabilityCalendarModel(
            [
                Blok(WibMidnightUtc(2026, 8, 12), WibMidnightUtc(2026, 8, 12)),
                Blok(WibMidnightUtc(2026, 8, 16), WibMidnightUtc(2026, 8, 14))
            ],
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20));

        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 12)));
        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 14)));
        Assert.False(model.IsBlocked(new DateOnly(2026, 8, 15)));
    }

    [Fact]
    public void Navigasi_bulan_terkurung_di_jendela()
    {
        var model = new AvailabilityCalendarModel(
            [], new DateOnly(2026, 8, 9), new DateOnly(2026, 11, 7));

        Assert.False(model.CanGoPrev);
        Assert.True(model.CanGoNext);

        model.PrevMonth();
        Assert.Equal(new DateOnly(2026, 8, 1), model.ViewMonth);

        model.NextMonth();
        model.NextMonth();
        model.NextMonth();
        Assert.Equal(new DateOnly(2026, 11, 1), model.ViewMonth);
        Assert.False(model.CanGoNext);

        model.NextMonth();
        Assert.Equal(new DateOnly(2026, 11, 1), model.ViewMonth);
    }
}
