using System.Globalization;
using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Formats numbers and dates for people, in the request's language (<see cref="AppCulture"/>; RequestTextMiddleware makes
/// it the CurrentUICulture). Use it for every number or date a page shows: <c>@total.Display("N0")</c>,
/// <c>@TextCulture.Date(day, DateStyle.Date)</c>. Values a machine reads (input values, data-* attributes, URLs, JSON)
/// keep the invariant culture (<c>ToString("yyyy-MM-dd")</c>, <c>ToString("O")</c>), which CurrentCulture always is.
/// <c>Guards/DisplayFormatTests</c> fails on a display pattern in Pages formatted without a culture.
/// </summary>
public static class TextCulture
{
    /// <summary>The culture of the request's language (English outside a request or with any other UI culture).</summary>
    public static CultureInfo Current =>
        AppCulture.LanguageOf(CultureInfo.CurrentUICulture) is not null ? CultureInfo.CurrentUICulture : AppCulture.For(AppLanguage.English);

    /// <summary>A number or date with a .NET format, in the request's language.</summary>
    public static string Display(this IFormattable value, string format) => value.ToString(format, Current);

    /// <summary>A calendar date in the style of the request's language. No time zone math: pass the day or moment already
    /// in the zone it belongs to.</summary>
    public static string Date(DateTime value, DateStyle style) => value.ToString(Pattern(Current, style), Current);

    /// <inheritdoc cref="Date(DateTime, DateStyle)"/>
    public static string Date(DateOnly value, DateStyle style) => Date(value.ToDateTime(TimeOnly.MinValue), style);

    /// <inheritdoc cref="Date(DateTime, DateStyle)"/>
    public static string Date(DateTimeOffset value, DateStyle style) => value.ToString(Pattern(Current, style), Current);

    /// <summary>The pattern of a style for a culture: English puts the month first ("May 5, 2026"), French and Arabic the
    /// day ("5 mai 2026", "5 مايو 2026").</summary>
    public static string Pattern(CultureInfo culture, DateStyle style)
    {
        var monthFirst = AppCulture.LanguageOf(culture) is null or AppLanguage.English;
        return style switch
        {
            DateStyle.Date => monthFirst ? "MMM d, yyyy" : "d MMM yyyy",
            DateStyle.MonthDay => monthFirst ? "MMM d" : "d MMM",
            DateStyle.WeekdayDate => monthFirst ? "ddd, MMM d, yyyy" : "ddd d MMM yyyy",
            DateStyle.MonthDayTime => monthFirst ? "MMM d, HH:mm" : "d MMM HH:mm",
            DateStyle.MonthShort => "MMM",
            DateStyle.MonthYear => "MMM yyyy",
            _ => "d"
        };
    }
}
