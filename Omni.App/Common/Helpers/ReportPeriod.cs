using Microsoft.Extensions.Localization;
using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>The period dropdown shared by the Dashboard and Reports pages (Pages/Shared/_PeriodPicker): its choices, the
/// inclusive viewer-local dates each one covers, and the [from, to) UTC instants the reporting queries take.</summary>
public static class ReportPeriod
{
    public const string ThisWeek = "this_week";
    public const string LastWeek = "last_week";
    public const string ThisMonth = "this_month";
    public const string LastMonth = "last_month";
    public const string Last7Days = "7d";
    public const string Last30Days = "30d";
    public const string AllTime = "all";
    public const string Custom = "custom";

    /// <summary>The dropdown's choices, in display order.</summary>
    public static readonly IReadOnlyList<string> All = [ThisWeek, LastWeek, ThisMonth, LastMonth, Last7Days, Last30Days, AllTime, Custom];

    /// <summary>Inclusive viewer-local start/end dates for a period (nulls = all time). Weeks start on Monday. An unknown
    /// period falls back to this month (the returned Period says which one was used).</summary>
    public static (string Period, DateOnly? From, DateOnly? To) Resolve(string? period, DateOnly? from, DateOnly? to, DateOnly today)
    {
        var mondayOffset = ((int)today.DayOfWeek + 6) % 7; // Monday = 0 … Sunday = 6
        var thisMonday = today.AddDays(-mondayOffset);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        switch (period)
        {
            case ThisWeek: return (period, thisMonday, today);
            case LastWeek: return (period, thisMonday.AddDays(-7), thisMonday.AddDays(-1));
            case ThisMonth: return (period, firstOfMonth, today);
            case LastMonth: return (period, firstOfMonth.AddMonths(-1), firstOfMonth.AddDays(-1));
            case Last7Days: return (period, today.AddDays(-6), today);
            case Last30Days: return (period, today.AddDays(-29), today);
            case AllTime: return (period, null, null);
            case Custom:
                if (from is null && to is null) return (period, null, null);
                var start = from ?? to!.Value;
                var end = to ?? today;
                return start <= end ? (period, start, end) : (period, end, start);
            default:
                return (ThisMonth, firstOfMonth, today);
        }
    }

    /// <summary>The range in words for the page, e.g. "Sep 1 – Sep 30, 2026", one date for a single day, or "All time".</summary>
    public static string Label(IStringLocalizer text, DateOnly? from, DateOnly? to) =>
        from is null || to is null
            ? text[AllTimeKey].Value
            : from == to
                ? TextCulture.Date(from.Value, DateStyle.Date)
                : $"{TextCulture.Date(from.Value, DateStyle.MonthDay)} – {TextCulture.Date(to.Value, DateStyle.Date)}";

    private const string AllTimeKey = "dashboard.period.all";

    /// <summary>Viewer-local midnight of <paramref name="date"/>, as UTC.</summary>
    public static DateTimeOffset LocalMidnightUtc(DateOnly date, TimeZoneInfo tz) =>
        ViewerTimeZone.ToUtc(date.ToDateTime(TimeOnly.MinValue), tz);

    /// <summary>[from, to) UTC instants for inclusive local dates: the upper bound is the midnight after the last day.</summary>
    public static (DateTimeOffset? From, DateTimeOffset? To) ToUtcRange(DateOnly? from, DateOnly? to, TimeZoneInfo tz) =>
        (from is null ? null : LocalMidnightUtc(from.Value, tz), to is null ? null : LocalMidnightUtc(to.Value.AddDays(1), tz));
}
