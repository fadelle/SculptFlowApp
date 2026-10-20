namespace Omni.App.Tests.Helpers;

/// <summary>The contact's language is stored as a two-letter ISO 639-1 code (plan D8): names, three-letter codes and tags
/// map to it, Arabizi and Lebanese count as Arabic, anything else is null. The same cases run against the database's
/// crm.normalize_language in ContactLanguageWalkTests.</summary>
public class ContactLanguageTests
{
    public static TheoryData<string?, string?> Cases => new()
    {
        { "ar", "ar" }, { " AR ", "ar" }, { "Arabic", "ar" }, { "arabizi", "ar" }, { "Arabizi", "ar" }, { "Lebanese", "ar" },
        { "lebanese  arabic", "ar" }, { "Franco_Arabic", "ar" }, { "عربي", "ar" }, { "العربية", "ar" }, { "ar-LB", "ar" },
        { "ar-Latn", "ar" }, { "ara", "ar" },
        { "fr", "fr" }, { "French", "fr" }, { "français", "fr" }, { "francais", "fr" }, { "fr-CA", "fr" }, { "fr_FR", "fr" },
        { "en", "en" }, { "English", "en" }, { "en-US", "en" }, { "eng", "en" }, { "الإنجليزية", "en" },
        { "es", "es" }, { "Spanish", "es" }, { "it", "it" }, { "pt-BR", "pt" }, { "zh-Hant-TW", "zh" }, { "farsi", "fa" },
        { null, null }, { "", null }, { "   ", null }, { "xx", null }, { "Klingon", null }, { "e", null }, { "e n", null },
        { "en-", null }, { "-en", null }, { "fr--CA", null }, { "hello world", null }, { "123", null }, { "😀", null },
        { "en;q=0.8", null }, { "'; drop table x;--", null }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Normalize_maps_names_codes_and_tags_to_a_two_letter_code(string? raw, string? expected)
    {
        Assert.Equal(expected, ContactLanguage.Normalize(raw));
    }

    [Fact]
    public void Every_name_maps_to_a_real_code_and_every_code_maps_to_itself()
    {
        Assert.Equal(184, ContactLanguage.Codes.Count);
        Assert.All(ContactLanguage.Codes, code => Assert.Equal(code, ContactLanguage.Normalize(code)));
        Assert.All(ContactLanguage.Names, pair =>
        {
            Assert.Contains(pair.Value, ContactLanguage.Codes);
            Assert.Equal(pair.Value, ContactLanguage.Normalize(pair.Key));
            Assert.Equal(pair.Key, pair.Key.Trim().ToLowerInvariant()); // keys are stored the way Normalize looks them up
        });
    }
}
