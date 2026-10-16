using Microsoft.Net.Http.Headers;
using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>Picks the language a request is shown in: the first preferred language that is supported (the user's own
/// setting, then the client's default), else the best supported one the browser asks for (Accept-Language, by quality),
/// else English.</summary>
public static class LanguagePicker
{
    public static string Pick(IEnumerable<string?> preferred, string? acceptLanguage, IReadOnlyCollection<string> supported)
    {
        foreach (var language in preferred)
        {
            var code = Normalize(language);
            if (code is not null && supported.Contains(code)) return code;
        }

        if (!string.IsNullOrWhiteSpace(acceptLanguage) && StringWithQualityHeaderValue.TryParseList([acceptLanguage], out var values))
        {
            foreach (var value in values.OrderByDescending(v => v.Quality ?? 1))
            {
                if (value.Quality == 0) continue;
                var code = Normalize(value.Value.Value);
                if (code is not null && supported.Contains(code)) return code;
            }
        }

        return AppLanguage.English;
    }

    /// <summary>"fr-CA" → "fr"; null for blanks and "*".</summary>
    private static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language == "*") return null;
        var dash = language.IndexOf('-');
        return (dash > 0 ? language[..dash] : language).Trim().ToLowerInvariant();
    }
}
