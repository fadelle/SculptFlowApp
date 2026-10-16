using System.Globalization;
using System.Text.RegularExpressions;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Fills a UI string's placeholders: <c>{0}</c>, <c>{1}</c>… from the arguments, and term names (<c>{contact}</c>,
/// <c>{Contacts}</c>…) from the business type's words (<see cref="TermPlaceholders"/>). A placeholder with no value is
/// left as written, so a mistake shows on screen instead of throwing. wwwroot/js/omni-text.js does the same in the browser.
/// </summary>
public static partial class TextFormat
{
    public static string Apply(string template, IReadOnlyDictionary<string, string> terms, IReadOnlyList<object?>? args)
    {
        if (template.IndexOf('{') < 0) return template;
        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (char.IsAsciiDigit(name[0]))
            {
                var index = int.Parse(name, CultureInfo.InvariantCulture);
                return args is not null && index < args.Count && args[index] is { } value
                    ? Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
                    : match.Value;
            }
            return terms.TryGetValue(name, out var term) ? term : match.Value;
        });
    }

    /// <summary>Every placeholder name in a template, without braces ("0", "contact"…).</summary>
    public static IEnumerable<string> PlaceholdersIn(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\{([0-9]+|[A-Za-z]+)\}")]
    private static partial Regex Placeholder();
}
