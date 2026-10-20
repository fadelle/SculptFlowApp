using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>
/// The plural category of a count, per language (CLDR rules for whole numbers): English one/other; French "one" for 0 and
/// 1; Arabic zero, one, two, few (n % 100 in 3–10), many (n % 100 in 11–99), other. A plural text is a family of keys
/// "key.{category}": en.json has .one and .other; a translation may add the categories its language uses, and a missing
/// one falls back to .other. The browser does the same with Intl.PluralRules (omniText.plural).
/// </summary>
public static class PluralRules
{
    public const string Zero = "zero";
    public const string One = "one";
    public const string Two = "two";
    public const string Few = "few";
    public const string Many = "many";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> Categories = [Zero, One, Two, Few, Many, Other];

    public static string Category(string language, long count)
    {
        var n = Math.Abs(count);
        return language switch
        {
            AppLanguage.French => n <= 1 ? One : Other,
            AppLanguage.Arabic => n switch
            {
                0 => Zero,
                1 => One,
                2 => Two,
                _ when n % 100 is >= 3 and <= 10 => Few,
                _ when n % 100 is >= 11 and <= 99 => Many,
                _ => Other
            },
            _ => n == 1 ? One : Other
        };
    }
}
