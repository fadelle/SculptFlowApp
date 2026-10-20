using Microsoft.Net.Http.Headers;
using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>Picks the language a request is shown in: the first preferred language that is supported (the user's own
/// setting, then the client's default), else the best supported one the browser asks for (Accept-Language, by quality),
/// else English.</summary>
public static class LanguagePicker
{
    /// <summary>The language a visitor picked on the sign-in pages (POST /language); signed-in users keep theirs in
    /// users.language.</summary>
    public const string CookieName = "omni.lang";

    /// <summary>Translation review mode (Localization:ShowTextKeys): ?textKeys=1 turns it on for the browser (a session
    /// cookie), ?textKeys=0 off.</summary>
    public const string TextKeysQuery = "textKeys";
    public const string TextKeysCookieName = "omni.textkeys";

    /// <summary>The languages a Localization:Languages value turns on ("en, AR" → en, ar), English always first and in.</summary>
    public static IReadOnlyList<string> Enabled(string? setting) =>
        new[] { AppLanguage.English }
            .Concat((setting ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal).ToList();

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
