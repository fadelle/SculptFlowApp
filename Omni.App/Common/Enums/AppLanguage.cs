namespace Omni.App.Common.Enums;

/// <summary>Languages the dashboard can be shown in (two-letter codes; users.language, clients.default_language, business
/// type names, terminology and stage labels). English is the fallback for anything missing. A language is only offered
/// once its strings file exists (Resources/i18n/{code}.json) and the Localization:Languages setting lists it.</summary>
public static class AppLanguage
{
    public const string English = "en";
    public const string Arabic = "ar";
    public const string French = "fr";

    public static readonly IReadOnlyList<string> All = [English, Arabic, French];

    /// <summary>"rtl" for right-to-left languages, else "ltr" (the html dir attribute).</summary>
    public static string Direction(string language) => language == Arabic ? "rtl" : "ltr";

    /// <summary>Each language in its own name, for the language pickers (never translated).</summary>
    public static string NativeName(string language) => language switch
    {
        English => "English",
        Arabic => "العربية",
        French => "Français",
        _ => language
    };

    /// <summary>The BCP 47 tag the browser formats dates and numbers with (Intl, toLocale…): Arabic with Latin digits and
    /// the Gregorian calendar.</summary>
    public static string BrowserLocale(string language) => language switch
    {
        Arabic => "ar-u-nu-latn-ca-gregory",
        French => "fr-FR",
        _ => "en-US"
    };
}
