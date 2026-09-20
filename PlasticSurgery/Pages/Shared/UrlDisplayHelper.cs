namespace PlasticSurgery.Pages.Shared;

/// <summary>Display-only formatting for URLs shown in tables. The stored/linked URL is never changed —
/// this only makes percent-encoded addresses (e.g. Arabic paths) readable.</summary>
public static class UrlDisplayHelper
{
    /// <summary>Decodes %XX sequences ("/ar/%D8%A7…" → "/ar/الرئيسية") and drops the scheme for a shorter, cleaner
    /// label. Falls back to the original text if it isn't valid percent-encoding.</summary>
    public static string Readable(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        string decoded;
        try { decoded = Uri.UnescapeDataString(url); }
        catch (UriFormatException) { decoded = url; }

        foreach (var scheme in new[] { "https://", "http://" })
        {
            if (decoded.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return decoded[scheme.Length..];
        }
        return decoded;
    }
}
