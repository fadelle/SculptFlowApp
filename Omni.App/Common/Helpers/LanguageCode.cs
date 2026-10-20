using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>Checks a dashboard language someone picked (users.language, clients.default_language).</summary>
public static class LanguageCode
{
    /// <summary>"AR " → "ar"; null for blank (no choice); ArgumentException (languages.errors.unknown) for anything that
    /// isn't a dashboard language (<see cref="AppLanguage.All"/>).</summary>
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var code = language.Trim().ToLowerInvariant();
        return AppLanguage.All.Contains(code) ? code : throw new ArgumentException("languages.errors.unknown").WithArgs(language.Trim());
    }
}
