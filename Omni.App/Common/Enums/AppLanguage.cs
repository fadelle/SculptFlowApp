namespace Omni.App.Common.Enums;

/// <summary>Languages the dashboard can be shown in (two-letter codes; clients.default_language, business type names,
/// terminology and stage labels). English is the fallback for anything missing. A language is only offered once its
/// strings file exists (Resources/i18n/{code}.json).</summary>
public static class AppLanguage
{
    public const string English = "en";
    public const string Arabic = "ar";
    public const string French = "fr";

    public static readonly IReadOnlyList<string> All = [English, Arabic, French];

    /// <summary>"rtl" for right-to-left languages, else "ltr" (the html dir attribute).</summary>
    public static string Direction(string language) => language == Arabic ? "rtl" : "ltr";
}
