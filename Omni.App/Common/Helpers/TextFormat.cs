using System.Globalization;
using System.Text.RegularExpressions;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Fills a UI string's placeholders: <c>{0}</c>, <c>{1}</c>… from the arguments (<c>{0:N0}</c> formats a number with a .NET
/// format, in the language's culture), and term names (<c>{contact}</c>,
/// <c>{Contacts}</c>…) from the business type's words (<see cref="TermPlaceholders"/>). A placeholder with no value is
/// left as written, so a mistake shows on screen instead of throwing. wwwroot/js/omni-text.js does the same in the browser.
/// </summary>
public static partial class TextFormat
{
    /// <summary>Numbers and dates among the arguments are written with <paramref name="culture"/> (the request's display
    /// culture; English when null).</summary>
    public static string Apply(string template, IReadOnlyDictionary<string, string> terms, IReadOnlyList<object?>? args, CultureInfo? culture = null)
    {
        if (template.IndexOf('{') < 0) return template;
        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (char.IsAsciiDigit(name[0]))
            {
                var index = int.Parse(name, CultureInfo.InvariantCulture);
                var format = match.Groups[2].Success ? match.Groups[2].Value : null;
                var display = culture ?? AppCulture.For(null);
                return args is not null && index < args.Count && args[index] is { } value
                    ? format is not null && value is IFormattable formattable
                        ? formattable.ToString(format, display)
                        : Convert.ToString(value, display) ?? string.Empty
                    : match.Value;
            }
            if (match.Groups[2].Success) return match.Value;
            return terms.TryGetValue(name, out var term) ? term : match.Value;
        });
    }

    /// <summary>Every placeholder name in a template, without braces ("0", "contact"…).</summary>
    public static IEnumerable<string> PlaceholdersIn(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\{([0-9]+|[A-Za-z]+)(?::([A-Za-z0-9#.,]+))?\}")]
    private static partial Regex Placeholder();
}
