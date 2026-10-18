using Microsoft.Extensions.Localization;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Turns a stored status value (qualification, booking status, campaign status, template category…) into the label people
/// read: <c>@T.ValueLabel(contact.Qualification)</c> reads <c>values.{value in camelCase}</c> from the strings files
/// ("needs_human" → <c>values.needsHuman</c>). A value with no key (a status added later, a free-text source) shows made
/// readable ("some_new_status" → "Some new status"). Contact stages are not here: their labels come from the client's
/// business type (StageDisplay). Presentational only; the stored values (Database/schema.sql's checks) are unchanged.
/// </summary>
public static class FilterLabelHelper
{
    public const string KeyPrefix = "values.";

    public static string ValueLabel(this IStringLocalizer text, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var key = KeyOf(value);
        if (key is not null)
        {
            var label = text[key];
            if (!label.ResourceNotFound) return label.Value;
        }
        return Readable(value);
    }

    /// <summary>"needs_human" / "NO_SHOW" / "Needs human" → "values.needsHuman"; null when the value has no letters.</summary>
    public static string? KeyOf(string value) => KeyPart(value) is { } part ? KeyPrefix + part : null;

    /// <summary>A stored value as one key segment: "needs_human" → "needsHuman"; null when the value has no letters.</summary>
    public static string? KeyPart(string value)
    {
        var words = value.Trim().ToLowerInvariant().Split(['_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsAsciiLetterOrDigit).ToArray())).Where(w => w.Length > 0).ToList();
        if (words.Count == 0 || !char.IsAsciiLetter(words[0][0])) return null;
        return words[0] + string.Concat(words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>The value made readable: underscores to spaces, first letter capitalized ("—" for nothing).</summary>
    public static string Readable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var spaced = value.Trim().Replace('_', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
