using System.Globalization;
using System.Text;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Recognises a contact's message that is only a stop or start word (settings OptOut:StopKeywords / StartKeywords). The
/// whole message must be the word: "stop", "STOP!", "/stop", «Arrêt», «إيقاف» match; "please stop calling me" does not.
/// Matching ignores case, accents and Arabic diacritics, the alef forms (أ إ آ → ا), tatweel, surrounding punctuation and
/// repeated spaces.
/// </summary>
public static class OptOutKeywords
{
    /// <summary>A longer message is never only a keyword (saves normalising long texts).</summary>
    private const int MaxLength = 40;

    /// <summary>true = a stop word, false = a start word, null = neither.</summary>
    public static bool? Match(string? text, string stopKeywords, string startKeywords)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return null;
        var normalized = Normalize(text);
        if (normalized.Length == 0) return null;
        if (List(stopKeywords).Contains(normalized)) return true;
        if (List(startKeywords).Contains(normalized)) return false;
        return null;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark || c == 'ـ') continue; // accents, harakat, tatweel
            builder.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                _ when char.IsWhiteSpace(c) => ' ',
                _ => char.ToLowerInvariant(c)
            });
        }
        var words = builder.ToString()
            .Trim(' ', '.', ',', '!', '?', '؟', '،', '/', '"', '\'', '«', '»', '(', ')', '-', '*', ':', ';')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }

    private static HashSet<string> List(string keywords) =>
        keywords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
}
