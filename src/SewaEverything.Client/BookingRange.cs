namespace SewaEverything.Client;

public sealed record BookingRangeResult(DateTimeOffset Start, DateTimeOffset End, string? Error)
{
    public bool IsValid => Error is null;

    public static BookingRangeResult Invalid(string error) => new(default, default, error);
}

public static class BookingRange
{
    public static readonly TimeSpan Wib = TimeSpan.FromHours(7);

    public static BookingRangeResult ForDays(DateOnly firstDay, DateOnly lastDay)
    {
        if (lastDay < firstDay)
        {
            return BookingRangeResult.Invalid("Tanggal selesai tidak boleh sebelum tanggal mulai.");
        }

        var start = Midnight(firstDay);
        var endDay = lastDay.AddDays(1);
        var end = Midnight(endDay);

        var now = DateTimeOffset.UtcNow.ToOffset(Wib);

        if (start < now)
        {
            start = now.AddMinutes(2);
        }

        return end <= start
            ? BookingRangeResult.Invalid("Rentang sewanya sudah lewat. Pilih tanggal yang belum berlalu.")
            : new BookingRangeResult(start, end, null);
    }

    public static BookingRangeResult ForHours(DateOnly day, int startHour, int endHour)
    {
        if (endHour <= startHour)
        {
            return BookingRangeResult.Invalid("Jam selesai harus setelah jam mulai.");
        }

        var start = new DateTimeOffset(day.Year, day.Month, day.Day, startHour, 0, 0, Wib);
        var end = Midnight(day).AddHours(endHour);

        var now = DateTimeOffset.UtcNow.ToOffset(Wib);

        return start < now
            ? BookingRangeResult.Invalid("Jam mulai sudah lewat. Pilih waktu yang belum berlalu.")
            : new BookingRangeResult(start, end, null);
    }

    public static BookingRangeResult For(
        string priceUnit, DateOnly firstDay, DateOnly lastDay, int startHour = 9, int endHour = 12) =>
        priceUnit == "hour"
            ? ForHours(firstDay, startHour, endHour)
            : ForDays(firstDay, lastDay);

    public static TimeSpan? UnitLength(string priceUnit) => priceUnit switch
    {
        "hour"  => TimeSpan.FromHours(1),
        "day"   => TimeSpan.FromDays(1),
        "week"  => TimeSpan.FromDays(7),
        "month" => TimeSpan.FromDays(30),
        _ => null
    };

    public static int Units(string priceUnit, DateTimeOffset start, DateTimeOffset end)
    {
        if (UnitLength(priceUnit) is not { } length || end <= start)
        {
            return 0;
        }

        var units = ((end - start).Ticks + length.Ticks - 1) / length.Ticks;

        return (int)Math.Max(1, units);
    }

    public static int Units(string priceUnit, BookingRangeResult range) =>
        range.IsValid ? Units(priceUnit, range.Start, range.End) : 0;

    private static DateTimeOffset Midnight(DateOnly day) =>
        new(day.Year, day.Month, day.Day, 0, 0, 0, Wib);
}
