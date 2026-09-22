using System.Text;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>
/// The ONE place a URL becomes an identity. Everything the crawler stores or compares
/// (knowledge_website_pages.normalized_url, the source's normalized_start_url) goes through here.
///
/// Rules: absolute http/https only; no credentials; scheme + host lower-cased (IDN → punycode); default
/// ports dropped; fragment removed; path dot-segments resolved, duplicate slashes collapsed, percent-escapes
/// upper-cased, "index.html/.htm/.php" folded into its directory, trailing slash removed except for the root
/// (so /page and /page/ are one URL); tracking parameters (utm_*, fbclid, gclid, …) removed while every other
/// query parameter is KEPT (some sites use them for real content) and the survivors are sorted so parameter
/// order can't create a second identity. Path CASE is preserved (paths are case-sensitive).
/// </summary>
public static partial class UrlNormalizer
{
    public const int MaxUrlLength = 2048;

    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "gbraid", "wbraid", "msclkid", "yclid", "dclid", "igshid", "mc_cid", "mc_eid",
        "_hsenc", "_hsmi", "mkt_tok", "vero_id", "srsltid", "ref_src"
    };

    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".ico", ".bmp", ".tif", ".tiff", ".avif", ".heic",
        ".css", ".js", ".mjs", ".map", ".json", ".xml", ".rss", ".atom", ".txt", ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".mp4", ".m4a", ".m4v", ".webm", ".mov", ".avi", ".wav", ".ogg", ".flac", ".wmv",
        ".zip", ".rar", ".7z", ".gz", ".tar", ".tgz", ".exe", ".dmg", ".msi", ".apk", ".iso", ".bin", ".swf", ".ics"
    };

    /// <summary>Documents we deliberately don't crawl (the Upload Document feature handles those).</summary>
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".csv"
    };

    /// <summary>Normalizes <paramref name="raw"/> (resolved against <paramref name="baseUri"/> if relative).
    /// Returns null when it isn't a crawlable http(s) URL.</summary>
    public static string? Normalize(string? raw, Uri? baseUri = null) =>
        TryNormalize(raw, baseUri, out var normalized) ? normalized : null;

    public static bool TryNormalize(string? raw, Uri? baseUri, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();

        Uri? uri;
        if (baseUri is not null)
        {
            if (!Uri.TryCreate(baseUri, raw, out uri)) return false;
        }
        else if (!Uri.TryCreate(raw, UriKind.Absolute, out uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;      // never carry credentials
        var host = HostForUrl(uri);
        if (host.Length == 0) return false;

        var sb = new StringBuilder(raw.Length + 16);
        sb.Append(uri.Scheme).Append("://").Append(host);
        if (!uri.IsDefaultPort) sb.Append(':').Append(uri.Port);
        sb.Append(NormalizePath(uri.AbsolutePath));

        var query = NormalizeQuery(uri.Query);
        if (query.Length > 0) sb.Append('?').Append(query);
        // Fragment intentionally dropped.

        if (sb.Length > MaxUrlLength) return false;
        normalized = sb.ToString();
        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        path = MultiSlash().Replace(path, "/");
        path = PercentEscape().Replace(path, m => m.Value.ToUpperInvariant());

        // "/services/index.html" is the same page as "/services/".
        var lastSlash = path.LastIndexOf('/');
        var last = path[(lastSlash + 1)..];
        if (last.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            || last.Equals("index.htm", StringComparison.OrdinalIgnoreCase)
            || last.Equals("index.php", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..(lastSlash + 1)];
        }

        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');
        return path.Length == 0 ? "/" : path;
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?") return string.Empty;
        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTrackingParam(KeyOf(p)))
            .Select(p => PercentEscape().Replace(p, m => m.Value.ToUpperInvariant()))
            .OrderBy(KeyOf, StringComparer.Ordinal)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
        return string.Join('&', parts);
    }

    private static string KeyOf(string pair)
    {
        var eq = pair.IndexOf('=');
        var key = eq < 0 ? pair : pair[..eq];
        try { return Uri.UnescapeDataString(key).ToLowerInvariant(); } catch (UriFormatException) { return key.ToLowerInvariant(); }
    }

    private static bool IsTrackingParam(string key) =>
        key.StartsWith("utm_", StringComparison.Ordinal) || TrackingParams.Contains(key);

    /// <summary>The host as it must appear in a URL: lower-case, punycode for IDN, and IPv6 literals in [brackets].</summary>
    private static string HostForUrl(Uri uri) =>
        uri.HostNameType == UriHostNameType.IPv6 ? "[" + uri.IdnHost.Trim('[', ']').ToLowerInvariant() + "]" : uri.IdnHost.ToLowerInvariant();

    /// <summary>The host without a leading "www." — "example.com" and "www.example.com" are one site.</summary>
    public static string BareHost(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..].ToLowerInvariant() : host.ToLowerInvariant();

    public static bool IsSameSite(string hostA, string hostB) =>
        string.Equals(BareHost(hostA), BareHost(hostB), StringComparison.OrdinalIgnoreCase);

    /// <summary>Same website = same host (ignoring "www.") AND same port. http↔https on their default ports
    /// count as the same site, but a different explicit port (example.com:8080) is a different site.</summary>
    public static bool IsSameSite(Uri a, Uri b) =>
        IsSameSite(a.IdnHost, b.IdnHost) && (a.Port == b.Port || (a.IsDefaultPort && b.IsDefaultPort));

    /// <summary>Rewrites the scheme/host/port of an already-normalized same-site URL to the site's effective
    /// origin (e.g. http://www.x.com/a → https://x.com/a), so www/non-www and http/https variants of a page
    /// share ONE identity. Returns the input unchanged if it isn't on the same site.</summary>
    public static string ToOrigin(string normalizedUrl, Uri origin)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var u) || !IsSameSite(u, origin)) return normalizedUrl;
        var sb = new StringBuilder();
        sb.Append(origin.Scheme).Append("://").Append(HostForUrl(origin));
        if (!origin.IsDefaultPort) sb.Append(':').Append(origin.Port);
        sb.Append(u.AbsolutePath);
        if (!string.IsNullOrEmpty(u.Query)) sb.Append(u.Query);
        return sb.ToString();
    }

    public static bool IsAssetUrl(string normalizedUrl) => HasExtension(normalizedUrl, AssetExtensions);
    public static bool IsDocumentUrl(string normalizedUrl) => HasExtension(normalizedUrl, DocumentExtensions);

    /// <summary>Blog-style listing pages (WordPress category/tag/author archives, pagination): a stream of post
    /// teasers and links, never the article text itself, so they make poor Knowledge Base entries (and poor
    /// Retrieval Benchmark test cases — see KnowledgeBenchmarkService's chunk sampling). Matched on whole path
    /// segments, not a substring, so e.g. "/my-tag-cloud/" is not caught.</summary>
    public static bool IsListingUrl(string normalizedUrl)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var u)) return false;
        var segments = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].ToLowerInvariant();
            if (seg is "category" or "categories" or "tag" or "tags" or "author") return true;
            if (seg == "page" && i + 1 < segments.Length && segments[i + 1].Length > 0 && segments[i + 1].All(char.IsAsciiDigit)) return true;
        }
        return false;
    }

    private static bool HasExtension(string normalizedUrl, HashSet<string> set)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var u)) return false;
        var ext = Path.GetExtension(u.AbsolutePath);
        return ext.Length > 0 && set.Contains(ext);
    }

    [GeneratedRegex("/{2,}")] private static partial Regex MultiSlash();
    [GeneratedRegex("%[0-9a-fA-F]{2}")] private static partial Regex PercentEscape();
}
