using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Localization;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Renders a stored UTC moment so wwwroot/js/local-time.js can show it in the VIEWER's own timezone.
/// The server only prints UTC (labelled "UTC") as the no-JS fallback; it never guesses the viewer's zone.
/// Formats: datetime, datetime-year, date, month-day, month-year, time (see local-time.js).
/// </summary>
public static class LocalTimeHelper
{
    private static readonly Dictionary<string, string> FallbackFormats = new()
    {
        ["datetime"] = "MMM d, HH:mm",
        ["datetime-year"] = "MMM d, yyyy HH:mm",
        ["date"] = "MMM d, yyyy",
        ["month-day"] = "MMM d",
        ["month-year"] = "MMMM yyyy",
        ["time"] = "HH:mm",
    };

    /// <summary><c>&lt;time&gt;</c> element showing <paramref name="value"/> in viewer-local time; <paramref name="empty"/> when null.
    /// With <paramref name="clientTimeZone"/> (booking pages) it shows client time instead, unless the viewer picked
    /// "My time" on the page's _TimeViewSwitch.</summary>
    public static IHtmlContent Time(DateTimeOffset? value, string format = "datetime", string empty = "—", string? clientTimeZone = null)
    {
        if (value is null) return new HtmlString(System.Net.WebUtility.HtmlEncode(empty));
        var utc = value.Value.ToUniversalTime();
        var fallback = Fallback(utc, format);
        var zone = clientTimeZone is null ? "" : $" data-client-tz=\"{System.Net.WebUtility.HtmlEncode(clientTimeZone)}\"";
        return new HtmlString($"<time datetime=\"{Iso(utc)}\" data-local=\"{format}\"{zone}>{System.Net.WebUtility.HtmlEncode(fallback)}</time>");
    }

    /// <summary><c>title="…" data-local-title="…"</c> attributes for a tooltip showing <paramref name="value"/> in viewer-local time.</summary>
    public static IHtmlContent Title(DateTimeOffset? value, string format = "datetime-year")
    {
        if (value is null) return HtmlString.Empty;
        var utc = value.Value.ToUniversalTime();
        return new HtmlString($"title=\"{System.Net.WebUtility.HtmlEncode(Fallback(utc, format))}\" data-local-title=\"{Iso(utc)}|{format}\"");
    }

    /// <summary>"N min/hours ago", "yesterday", or "N days ago" for a recent moment, falling back to an
    /// absolute date (via <see cref="Time"/>, so it's still rewritten into viewer-local time) once it passes
    /// <paramref name="dayCutoff"/> days old — so nothing shows an ever-growing "N days ago" forever. The
    /// relative text itself needs no timezone rewriting (it's a plain duration, not a wall-clock time).
    /// <paramref name="dayCutoff"/> and <paramref name="fallbackFormat"/> are tunable per page; the tiers
    /// below them are shared so the wording itself stays consistent everywhere.</summary>
    public static IHtmlContent Ago(IStringLocalizer text, DateTimeOffset when, int dayCutoff = 7, string fallbackFormat = "month-day")
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1) return Encoded(text[JustNowKey]);
        if (span.TotalMinutes < 60) return Encoded(text[MinutesKey, (int)span.TotalMinutes]);
        if (span.TotalHours < 24) return Encoded((int)span.TotalHours == 1 ? text[HourKey] : text[HoursKey, (int)span.TotalHours]);
        if (span.TotalDays < 2) return Encoded(text[YesterdayKey]);
        if (span.TotalDays < dayCutoff) return Encoded(text[DaysKey, (int)span.TotalDays]);
        return Time(when, fallbackFormat);
    }

    private const string JustNowKey = "time.ago.justNow";
    private const string MinutesKey = "time.ago.minutes";
    private const string HourKey = "time.ago.hour";
    private const string HoursKey = "time.ago.hours";
    private const string YesterdayKey = "time.ago.yesterday";
    private const string DaysKey = "time.ago.days";

    private static HtmlString Encoded(LocalizedString value) => new(System.Net.WebUtility.HtmlEncode(value.Value));

    public static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Fallback(DateTimeOffset utc, string format) =>
        utc.ToString(FallbackFormats.GetValueOrDefault(format, FallbackFormats["datetime"]), CultureInfo.InvariantCulture)
        + (format is "date" or "month-day" or "month-year" ? "" : " UTC");
}
