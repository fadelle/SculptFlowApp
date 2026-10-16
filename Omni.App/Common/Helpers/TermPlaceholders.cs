using System.Globalization;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Common.Helpers;

/// <summary>The placeholder names a UI string may use for a business type's words: for each term in
/// <see cref="TermName"/>, <c>{contact}</c> (singular), <c>{contacts}</c> (plural), and <c>{Contact}</c> /
/// <c>{Contacts}</c> with a capital first letter. Plural names add an "s" to the term name ("contacts").</summary>
public static class TermPlaceholders
{
    /// <summary>Every known placeholder name (without braces).</summary>
    public static readonly IReadOnlySet<string> Names = TermName.All
        .SelectMany(t => new[] { t, t + "s", Capitalize(t), Capitalize(t) + "s" })
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Placeholder name → text, from one language's words (every <see cref="TermName"/> must be present).</summary>
    public static IReadOnlyDictionary<string, string> Build(IReadOnlyDictionary<string, TermForms> terms)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in TermName.All)
        {
            if (!terms.TryGetValue(name, out var forms)) continue;
            map[name] = forms.One;
            map[name + "s"] = forms.Other;
            map[Capitalize(name)] = Capitalize(forms.One);
            map[Capitalize(name) + "s"] = Capitalize(forms.Other);
        }
        return map;
    }

    /// <summary>The text with its first letter upper-case (unchanged for scripts without case, such as Arabic).</summary>
    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
