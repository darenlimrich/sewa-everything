using System.Globalization;

namespace SewaEverything.Client;

public static class Format
{
    private static readonly NumberFormatInfo Rp = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3]
    };

    public static string Rupiah(decimal amount)
    {
        var whole = Math.Round(amount);
        return whole < 0
            ? "−Rp" + (-whole).ToString("#,0", Rp)
            : "Rp" + whole.ToString("#,0", Rp);
    }

    public static string RupiahAngka(decimal amount)
    {
        var whole = Math.Round(amount);
        return whole < 0
            ? "−" + (-whole).ToString("#,0", Rp)
            : whole.ToString("#,0", Rp);
    }

    public static string PriceUnit(string unit) => unit switch
    {
        "hour" => "/jam",
        "day" => "/hari",
        "week" => "/minggu",
        "month" => "/bulan",
        _ => "/" + unit
    };

    public static string PriceUnit(string unit, int units) =>
        units > 1 ? $"/{units} {Unit(unit)}" : PriceUnit(unit);

    public static string Unit(string unit) => unit switch
    {
        "hour" => "jam",
        "day" => "hari",
        "week" => "minggu",
        "month" => "bulan",
        _ => unit
    };

    private static readonly TimeSpan Wib = TimeSpan.FromHours(7);
    private static readonly string[] Bulan =
        ["Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des"];

    public static DateTime ToWib(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Wib).DateTime;

    public static string Tanggal(DateTime utc)
    {
        var w = ToWib(utc);
        return $"{w.Day} {Bulan[w.Month - 1]} {w.Year}";
    }

    public static string Waktu(DateTime utc)
    {
        var w = ToWib(utc);
        return $"{w.Day} {Bulan[w.Month - 1]} {w.Year}, {Jam(w)} WIB";
    }

    private static string Jam(DateTime wib) => wib.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Persen(decimal rate) =>
        (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static string Penilaian(double? average, int count) =>
        average is not null
            ? $"{NilaiRingkas(average)} · {count} penilaian"
            : "Belum ada penilaian";

    public static string Bintang(int rating) =>
        rating.ToString(CultureInfo.InvariantCulture) + "/5";

    public static string NilaiRingkas(double? average) =>
        average is { } rata
            ? rata.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')
            : string.Empty;

    public static string Range(DateTime startUtc, DateTime endUtc)
    {
        var s = ToWib(startUtc);
        var e = ToWib(endUtc);

        if (s.TimeOfDay != TimeSpan.Zero || e.TimeOfDay != TimeSpan.Zero || endUtc <= startUtc)
        {
            return $"{Tanggal(startUtc)} {Jam(s)} – {Tanggal(endUtc)} {Jam(e)}";
        }

        var hariTerakhir = endUtc.AddTicks(-1);

        return ToWib(hariTerakhir).Date == s.Date
            ? Tanggal(startUtc)
            : $"{Tanggal(startUtc)} – {Tanggal(hariTerakhir)}";
    }
}
