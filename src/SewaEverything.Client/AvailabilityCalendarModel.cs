using SewaEverything.Contracts;

namespace SewaEverything.Client;

public enum CalendarDayState
{
    Off,

    Blocked,

    Available,

    SelectedEdge,

    SelectedMid
}

public sealed class AvailabilityCalendarModel
{
    public static readonly string[] DayNames = ["Sen", "Sel", "Rab", "Kam", "Jum", "Sab", "Min"];

    public static readonly string[] MonthNames =
        ["Januari", "Februari", "Maret", "April", "Mei", "Juni",
         "Juli", "Agustus", "September", "Oktober", "November", "Desember"];

    private readonly HashSet<DateOnly> _blockedDays;
    private DateOnly _view;
    private DateOnly? _anchor;
    private bool _complete;

    public AvailabilityCalendarModel(
        IReadOnlyList<BlockedRangeResponse> blocked, DateOnly minDate, DateOnly maxDate)
    {
        MinDate = minDate;
        MaxDate = maxDate < minDate ? minDate : maxDate;

        _view = new DateOnly(MinDate.Year, MinDate.Month, 1);
        _blockedDays = ComputeBlockedDays(blocked, MinDate, MaxDate);
    }

    public DateOnly MinDate { get; }

    public DateOnly MaxDate { get; }

    public (DateOnly Start, DateOnly End)? Selection { get; private set; }

    public int SelectedDayCount =>
        Selection is { } s ? s.End.DayNumber - s.Start.DayNumber + 1 : 0;

    public DateOnly ViewMonth => _view;

    public string MonthLabel => $"{MonthNames[_view.Month - 1]} {_view.Year}";

    public int Lead => ((int)_view.DayOfWeek + 6) % 7;

    public IEnumerable<DateOnly> DaysInMonth =>
        Enumerable.Range(0, DateTime.DaysInMonth(_view.Year, _view.Month)).Select(i => _view.AddDays(i));

    public bool CanGoPrev => _view > new DateOnly(MinDate.Year, MinDate.Month, 1);

    public bool CanGoNext => _view < new DateOnly(MaxDate.Year, MaxDate.Month, 1);

    public void PrevMonth()
    {
        if (CanGoPrev)
        {
            _view = _view.AddMonths(-1);
        }
    }

    public void NextMonth()
    {
        if (CanGoNext)
        {
            _view = _view.AddMonths(1);
        }
    }

    public bool IsBlocked(DateOnly day) => _blockedDays.Contains(day);

    public CalendarDayState StateOf(DateOnly day)
    {
        if (day < MinDate || day > MaxDate)
        {
            return CalendarDayState.Off;
        }

        if (_blockedDays.Contains(day))
        {
            return CalendarDayState.Blocked;
        }

        if (Selection is { } sel && day >= sel.Start && day <= sel.End)
        {
            return day == sel.Start || day == sel.End
                ? CalendarDayState.SelectedEdge
                : CalendarDayState.SelectedMid;
        }

        return CalendarDayState.Available;
    }

    public bool Pick(DateOnly day)
    {
        if (day < MinDate || day > MaxDate || _blockedDays.Contains(day))
        {
            return false;
        }

        if (_anchor is null || _complete)
        {
            _anchor = day;
            Selection = (day, day);
            _complete = false;
            return true;
        }

        var lo = day < _anchor.Value ? day : _anchor.Value;
        var hi = day < _anchor.Value ? _anchor.Value : day;

        if (HasBlockedBetween(lo, hi))
        {
            _anchor = day;
            Selection = (day, day);
            _complete = false;
            return true;
        }

        Selection = (lo, hi);
        _complete = true;
        return true;
    }

    public void ClearSelection()
    {
        Selection = null;
        _anchor = null;
        _complete = false;
    }

    private bool HasBlockedBetween(DateOnly lo, DateOnly hi)
    {
        for (var n = lo.DayNumber; n <= hi.DayNumber; n++)
        {
            var d = DateOnly.FromDayNumber(n);

            if (d < MinDate || d > MaxDate || _blockedDays.Contains(d))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<DateOnly> ComputeBlockedDays(
        IReadOnlyList<BlockedRangeResponse> blocked, DateOnly min, DateOnly max)
    {
        var days = new HashSet<DateOnly>();

        foreach (var range in blocked)
        {
            var startUtc = AsUtc(range.StartsAt);
            var endUtc = AsUtc(range.EndsAt);

            if (endUtc <= startUtc)
            {
                continue;
            }

            var first = DateOnly.FromDateTime(ToWib(startUtc));
            var last = DateOnly.FromDateTime(ToWib(endUtc.AddTicks(-1)));

            if (first < min)
            {
                first = min;
            }

            if (last > max)
            {
                last = max;
            }

            for (var n = first.DayNumber; n <= last.DayNumber; n++)
            {
                days.Add(DateOnly.FromDayNumber(n));
            }
        }

        return days;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static DateTime ToWib(DateTime utc) =>
        new(Math.Clamp(utc.Ticks + BookingRange.Wib.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks),
            DateTimeKind.Unspecified);
}
