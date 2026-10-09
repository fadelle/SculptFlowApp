namespace PlasticSurgery.Common.Helpers;

/// <summary>Small text helpers for the views (docs/UI_GUIDE.md §0.4). Presentational only.</summary>
public static class UiTextHelper
{
    /// <summary>One or two letters for an avatar: "Sara Ali" → "SA", "sara@clinic.com" → "S", empty → "?".</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var text = name.Trim();
        var at = text.IndexOf('@');
        if (at > 0) text = text[..at];
        var words = text.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var first = char.ToUpperInvariant(words[0][0]);
        return words.Length > 1 && at < 0 ? $"{first}{char.ToUpperInvariant(words[^1][0])}" : first.ToString();
    }

    /// <summary>A whole amount with thousands separators ("12,500"), the same on every server: the production container
    /// runs with the invariant culture, where "C0" prints the generic currency sign (¤). No symbol, because the
    /// bookings behind a total can each carry their own currency.</summary>
    public static string Amount(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A short reference for an error page ("8F2C-1A90"): the first 8 characters of the request's trace id (the
    /// W3C id "00-&lt;trace&gt;-&lt;span&gt;-00", else the raw request id), which the server logs with the error.</summary>
    public static string ShortReference(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId)) return "";
        var parts = requestId.Split('-');
        var core = parts.Length >= 3 && parts[1].Length >= 8 ? parts[1] : new string(requestId.Where(char.IsLetterOrDigit).ToArray());
        core = core.Length > 8 ? core[..8] : core;
        return (core.Length == 8 ? $"{core[..4]}-{core[4..]}" : core).ToUpperInvariant();
    }

    /// <summary>
    /// Splits an error message into the plain sentence staff read and the technical part shown under "Technical
    /// details". Messages are built as "Could not connect Telegram: &lt;provider's reason&gt;", so the split is the first
    /// ": " when the text before it reads as one short sentence. Anything else stays whole, with no details.
    /// </summary>
    public static (string Summary, string? Details) SplitError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return ("", null);
        // ArgumentException appends " (Parameter 'request')" to its message: a code detail, not something staff can act on.
        var text = System.Text.RegularExpressions.Regex.Replace(message.Trim(), @"\s*\(Parameter '[^']*'\)$", "");
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 12 || colon > 140 || text[..colon].Contains(". ", StringComparison.Ordinal)) return (text, null);
        var details = text[(colon + 2)..].Trim();
        if (details.Length == 0) return (text, null);
        return (text[..colon].TrimEnd('.') + ".", details);
    }
}
