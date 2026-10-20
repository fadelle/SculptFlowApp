using System.Collections.Concurrent;
using System.Globalization;
using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>
/// The culture each dashboard language formats dates and numbers with (display only: parsing, form binding, JSON and
/// anything a machine reads stay invariant, because CurrentCulture is never changed). Arabic is built from "ar" with the
/// Gregorian calendar and Latin number symbols, so it prints "5 يناير 2026" and "1,234.5" (plain "ar-SA" would print
/// Hijri dates, and "ar" alone uses the Arabic decimal sign). RequestTextMiddleware makes the request's culture the
/// CurrentUICulture; <see cref="TextCulture"/> formats with it.
/// </summary>
public static class AppCulture
{
    private static readonly ConcurrentDictionary<string, CultureInfo> Cultures = new(StringComparer.Ordinal);

    /// <summary>The read-only display culture of a language (English for anything unknown).</summary>
    public static CultureInfo For(string? language) =>
        Cultures.GetOrAdd(language is AppLanguage.Arabic or AppLanguage.French ? language : AppLanguage.English, Build);

    /// <summary>The language a display culture belongs to, or null for any other culture.</summary>
    public static string? LanguageOf(CultureInfo culture) =>
        Cultures.FirstOrDefault(c => ReferenceEquals(c.Value, culture)).Key;

    private static CultureInfo Build(string language)
    {
        try
        {
            return language switch
            {
                AppLanguage.Arabic => Arabic(),
                AppLanguage.French => CultureInfo.ReadOnly(new CultureInfo("fr-FR", useUserOverride: false)),
                _ => CultureInfo.ReadOnly(new CultureInfo("en-US", useUserOverride: false))
            };
        }
        catch (CultureNotFoundException)
        {
            // A host without culture data (globalization-invariant mode) still works, with invariant (English) formats.
            return CultureInfo.ReadOnly((CultureInfo)CultureInfo.InvariantCulture.Clone());
        }
    }

    private static CultureInfo Arabic()
    {
        var culture = (CultureInfo)new CultureInfo("ar", useUserOverride: false).Clone();
        var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault()
                        ?? new GregorianCalendar(GregorianCalendarTypes.Localized);
        culture.DateTimeFormat.Calendar = gregorian;
        var latin = CultureInfo.InvariantCulture.NumberFormat;
        var numbers = culture.NumberFormat;
        numbers.NumberDecimalSeparator = numbers.CurrencyDecimalSeparator = numbers.PercentDecimalSeparator = latin.NumberDecimalSeparator;
        numbers.NumberGroupSeparator = numbers.CurrencyGroupSeparator = numbers.PercentGroupSeparator = latin.NumberGroupSeparator;
        numbers.NegativeSign = latin.NegativeSign;
        numbers.PositiveSign = latin.PositiveSign;
        numbers.PercentSymbol = latin.PercentSymbol;
        numbers.NumberNegativePattern = latin.NumberNegativePattern;
        numbers.NativeDigits = latin.NativeDigits;
        numbers.DigitSubstitution = DigitShapes.None;
        return CultureInfo.ReadOnly(culture);
    }
}
