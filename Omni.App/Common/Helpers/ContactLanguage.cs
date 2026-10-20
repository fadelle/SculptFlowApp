namespace Omni.App.Common.Helpers;

/// <summary>
/// The language a contact writes in (crm.contacts.preferred_language): a two-letter ISO 639-1 code, any language, not only
/// the dashboard ones. The AI, the dashboard and the copy of old data all store it through <see cref="Normalize"/>, so
/// "Arabic", "Arabizi", "Lebanese", "عربي" or "ar-LB" all become "ar". The database function crm.normalize_language
/// (Database/schema.sql) applies the same rules; ContactLanguageTests checks both against one list of cases.
/// </summary>
public static class ContactLanguage
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string French = "fr";

    /// <summary>Every ISO 639-1 code.</summary>
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(
        ("aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz ee el en eo " +
         "es et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik io is it iu ja jv ka kg ki " +
         "kj kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms mt my na nb nd ne ng nl nn no nr " +
         "nv ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th " +
         "ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi vo wa wo xh yi yo za zh zu").Split(' '), StringComparer.Ordinal);

    /// <summary>Language names (English, their own name, Arabic for the three dashboard languages) and three-letter codes
    /// the AI or an old record may write instead of a code. Arabizi and Lebanese written in Latin letters are Arabic.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["arabic"] = Arabic, ["arabe"] = Arabic, ["ara"] = Arabic, ["arabizi"] = Arabic, ["arabeezi"] = Arabic,
        ["franco"] = Arabic, ["franco-arabic"] = Arabic, ["franco arabic"] = Arabic, ["lebanese"] = Arabic, ["lebanese arabic"] = Arabic,
        ["levantine"] = Arabic, ["عربي"] = Arabic, ["عربية"] = Arabic, ["العربية"] = Arabic, ["لبناني"] = Arabic,
        ["english"] = English, ["anglais"] = English, ["eng"] = English, ["انجليزي"] = English, ["إنجليزي"] = English,
        ["الانجليزية"] = English, ["الإنجليزية"] = English,
        ["french"] = French, ["français"] = French, ["francais"] = French, ["fra"] = French, ["fre"] = French,
        ["فرنسي"] = French, ["الفرنسية"] = French,
        ["spanish"] = "es", ["español"] = "es", ["espanol"] = "es", ["spa"] = "es",
        ["german"] = "de", ["deutsch"] = "de", ["deu"] = "de", ["ger"] = "de",
        ["italian"] = "it", ["italiano"] = "it", ["ita"] = "it",
        ["portuguese"] = "pt", ["português"] = "pt", ["portugues"] = "pt", ["por"] = "pt",
        ["turkish"] = "tr", ["türkçe"] = "tr", ["turkce"] = "tr", ["tur"] = "tr",
        ["russian"] = "ru", ["русский"] = "ru", ["rus"] = "ru",
        ["armenian"] = "hy", ["hye"] = "hy", ["arm"] = "hy",
        ["persian"] = "fa", ["farsi"] = "fa", ["fas"] = "fa", ["per"] = "fa",
        ["kurdish"] = "ku", ["kur"] = "ku",
        ["hebrew"] = "he", ["heb"] = "he",
        ["greek"] = "el", ["ell"] = "el", ["gre"] = "el",
        ["dutch"] = "nl", ["nld"] = "nl", ["dut"] = "nl",
        ["urdu"] = "ur", ["urd"] = "ur",
        ["hindi"] = "hi", ["hin"] = "hi",
        ["chinese"] = "zh", ["mandarin"] = "zh", ["zho"] = "zh", ["chi"] = "zh",
    };

    /// <summary>
    /// The two-letter code for <paramref name="raw"/>, or null when it is blank or not understood. Case, extra spaces and
    /// underscores don't matter. A known name or three-letter code maps through <see cref="Names"/>; a language tag keeps
    /// its first part when that is an ISO 639-1 code ("fr-CA" → "fr", "ar-Latn" → "ar"); anything else is null.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = string.Join(' ', raw.Trim().ToLowerInvariant().Replace('_', '-')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (Names.TryGetValue(text, out var named)) return named;

        var parts = text.Split('-');
        if (parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiLetterOrDigit))) return null;
        return parts[0].Length == 2 && Codes.Contains(parts[0]) ? parts[0] : null;
    }
}
