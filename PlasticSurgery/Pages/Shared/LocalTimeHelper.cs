using System.Globalization;
using Microsoft.AspNetCore.Html;

namespace PlasticSurgery.Pages.Shared;

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
    /// With <paramref name="clinicTimeZone"/> (appointment pages) it shows clinic time instead, unless the viewer picked
    /// "My time" on the page's _TimeViewSwitch.</summary>
    public static IHtmlContent Time(DateTimeOffset? value, string format = "datetime", string empty = "—", string? clinicTimeZone = null)
    {
        if (value is null) return new HtmlString(System.Net.WebUtility.HtmlEncode(empty));
        var utc = value.Value.ToUniversalTime();
        var fallback = Fallback(utc, format);
        var zone = clinicTimeZone is null ? "" : $" data-clinic-tz=\"{System.Net.WebUtility.HtmlEncode(clinicTimeZone)}\"";
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
    public static IHtmlContent Ago(DateTimeOffset when, int dayCutoff = 7, string fallbackFormat = "month-day")
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1) return new HtmlString("just now");
        if (span.TotalMinutes < 60) return new HtmlString($"{(int)span.TotalMinutes} min ago");
        if (span.TotalHours < 24) return new HtmlString($"{(int)span.TotalHours} {((int)span.TotalHours == 1 ? "hour" : "hours")} ago");
        if (span.TotalDays < 2) return new HtmlString("yesterday");
        if (span.TotalDays < dayCutoff) return new HtmlString($"{(int)span.TotalDays} days ago");
        return Time(when, fallbackFormat);
    }

    public static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Fallback(DateTimeOffset utc, string format) =>
        utc.ToString(FallbackFormats.GetValueOrDefault(format, FallbackFormats["datetime"]), CultureInfo.InvariantCulture)
        + (format is "date" or "month-day" or "month-year" ? "" : " UTC");
}
