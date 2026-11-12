using Microsoft.Extensions.Localization;

namespace Omni.App.Common.Helpers;

/// <summary>
/// A problem text stored for every member to read (an integration's LastProblemMessage), kept as a strings-file key and
/// its arguments, so each reader sees it in their own language (QA t5-ops-14: the text was stored in the language of
/// whoever triggered it). Stored as <c>[[t:key|arg|…]]</c>; anything else is shown as written (a provider's own message).
/// </summary>
public static class StoredText
{
    private const string Prefix = "[[t:", Suffix = "]]";

    /// <summary>The stored form of <paramref name="key"/> with plain-text arguments.</summary>
    public static string Key(string key, params string[] args) =>
        Prefix + string.Join('|', new[] { key }.Concat(args.Select(a => a.Replace("|", "/").Replace("]]", "] ]")))) + Suffix;

    /// <summary>An exception to store for later readers (QA t5-ops-12): its key and plain arguments when it is one of our keyed
    /// messages, else its text in <paramref name="text"/>'s language as before (a provider's own words, a key with an argument
    /// that is itself a text). Never longer than <paramref name="maxLength"/>.</summary>
    public static string Of(Exception exception, IStringLocalizer text, int maxLength = 1000)
    {
        static string[] Plain(IEnumerable<object?> values) =>
            values.Select(a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray();
        var args = TextError.ArgsOf(exception);
        var stored = ProviderErrorText.For(exception) is { } known ? Key(known.Key, Plain(known.Args))
            : !text[exception.Message].ResourceNotFound && args.All(a => a is null or string or int or long or decimal or double)
                ? Key(exception.Message, Plain(args))
                : text.Localize(exception);
        if (stored.Length <= maxLength) return stored;
        var localized = text.Localize(exception);
        return localized.Length <= maxLength ? localized : localized[..maxLength];
    }

    /// <summary>The text for this reader: a stored key in their language, anything else unchanged; empty for none.</summary>
    public static string Show(string? stored, IStringLocalizer text)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal) || !stored.EndsWith(Suffix, StringComparison.Ordinal)) return stored;
        var parts = stored[Prefix.Length..^Suffix.Length].Split('|');
        return text[parts[0], parts.Skip(1).Cast<object>().ToArray()].Value;
    }
}
