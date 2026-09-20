using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>One paragraph-level piece of a page's readable text. List items are flagged so they render as a list.</summary>
public sealed record TextBlock(string Text, bool ListItem = false);

public sealed record ExtractedPage(
    string? Title,
    /// <summary>The raw href of rel=canonical, resolved to absolute (not yet normalized/validated).</summary>
    string? CanonicalHref,
    bool NoIndex,
    bool NoFollow,
    /// <summary>Absolute hrefs of every followable link on the page.</summary>
    IReadOnlyList<string> Links,
    IReadOnlyList<TextBlock> Blocks);

/// <summary>
/// HTML → readable text using a real HTML5 parser (AngleSharp) — never regex on markup. Picks the main
/// content region (main / [role=main] / article / known content containers, else body), drops everything that
/// isn't human-readable page content (script, style, hidden/aria-hidden nodes, nav, header, aside, forms
/// controls, cookie/consent/popup/menu/breadcrumb/share widgets) and keeps structure: headings, paragraphs,
/// lists ("- "), table rows ("a | b"), FAQ/details text, image alt text. Footer text is dropped EXCEPT lines
/// that look like contact details (phone, email, address, hours) so that information isn't lost; repeated
/// site-wide blocks are then removed across pages by the processor.
/// </summary>
public interface IHtmlContentExtractor
{
    ExtractedPage Extract(string html, Uri pageUrl);
}

public sealed partial class HtmlContentExtractor : IHtmlContentExtractor
{
    private static readonly HashSet<string> SkipTags = new(StringComparer.Ordinal)
    {
        "script", "style", "noscript", "template", "iframe", "svg", "canvas", "object", "embed", "head", "link", "meta",
        "input", "select", "textarea", "button", "option", "datalist", "nav", "aside", "dialog", "audio", "video", "map"
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "main", "ul", "ol", "dl", "dt", "dd", "h1", "h2", "h3", "h4", "h5", "h6",
        "table", "thead", "tbody", "tfoot", "tr", "blockquote", "pre", "figure", "figcaption", "details", "summary",
        "address", "fieldset", "legend", "caption", "hr", "body", "form", "header", "footer", "hgroup"
    };

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "cookie", "cookies", "consent", "gdpr", "ccpa", "popup", "modal", "lightbox", "breadcrumb", "breadcrumbs",
        "sidebar", "social", "share", "sharing", "newsletter", "subscribe", "navbar", "navigation", "menu", "topbar",
        "skip", "screenreader", "chatbot", "livechat"
    };

    private static readonly HashSet<string> NoiseRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "navigation", "banner", "complementary", "dialog", "alertdialog", "search"
    };

    private static readonly string[] ContentSelectors =
    {
        "main", "[role=main]", "article", "#content", "#main-content", "#main", ".entry-content", ".post-content",
        ".page-content", ".content", ".main-content", "#primary"
    };

    public ExtractedPage Extract(string html, Uri pageUrl)
    {
        var doc = new HtmlParser().ParseDocument(html);

        // <base href> changes how relative links resolve.
        var baseUri = pageUrl;
        var baseHref = doc.QuerySelector("base[href]")?.GetAttribute("href");
        if (!string.IsNullOrWhiteSpace(baseHref) && Uri.TryCreate(pageUrl, baseHref, out var b)) baseUri = b;

        var (noIndex, noFollow) = ReadRobotsMeta(doc);
        var title = Clean(doc.Title);
        if (string.IsNullOrEmpty(title)) title = Clean(doc.QuerySelector("h1")?.TextContent);

        string? canonical = null;
        var canonicalHref = doc.QuerySelector("link[rel~=canonical]")?.GetAttribute("href");
        if (!string.IsNullOrWhiteSpace(canonicalHref) && Uri.TryCreate(baseUri, canonicalHref.Trim(), out var c)) canonical = c.ToString();

        var links = new List<string>();
        if (!noFollow)
        {
            foreach (var a in doc.QuerySelectorAll("a[href]"))
            {
                var rel = a.GetAttribute("rel");
                if (rel is not null && rel.Contains("nofollow", StringComparison.OrdinalIgnoreCase)) continue;
                var href = a.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#')) continue;
                if (Uri.TryCreate(baseUri, href.Trim(), out var abs)) links.Add(abs.ToString());
            }
        }

        var body = doc.Body;
        var blocks = new List<TextBlock>();
        if (body is not null)
        {
            var root = PickRoot(body);
            var walker = new Walker(root.TextContent.Length, root);
            walker.WalkChildren(root);
            walker.Flush();
            blocks.AddRange(walker.Blocks);

            // Footer(s) sit outside the main region — keep only the contact-detail lines.
            foreach (var footer in body.QuerySelectorAll("footer, [role=contentinfo]"))
            {
                if (IsInside(footer, root)) continue;
                foreach (var line in footer.TextContent.Split('\n'))
                {
                    var t = Clean(line);
                    if (t.Length is > 3 and <= 160 && LooksLikeContact(t)) blocks.Add(new TextBlock(t));
                }
            }
        }

        return new ExtractedPage(title, canonical, noIndex, noFollow, links, blocks);
    }

    private static IElement PickRoot(IElement body)
    {
        var bodyLen = body.TextContent.Length;
        foreach (var selector in ContentSelectors)
        {
            var candidates = body.QuerySelectorAll(selector);
            // Several <article>s (a blog index) → the whole body is a better root than the first one.
            if (candidates.Length is 0 or > 1 && selector == "article") continue;
            var best = candidates.OrderByDescending(e => e.TextContent.Length).FirstOrDefault();
            if (best is not null && best.TextContent.Trim().Length >= Math.Min(200, bodyLen / 4)) return best;
        }
        return body;
    }

    private static bool IsInside(IElement el, IElement ancestor)
    {
        for (var p = el.ParentElement; p is not null; p = p.ParentElement) if (ReferenceEquals(p, ancestor)) return true;
        return ReferenceEquals(el, ancestor);
    }

    private static (bool NoIndex, bool NoFollow) ReadRobotsMeta(IDocument doc)
    {
        bool noindex = false, nofollow = false;
        foreach (var meta in doc.QuerySelectorAll("meta[name][content]"))
        {
            var name = meta.GetAttribute("name")?.ToLowerInvariant();
            if (name is not ("robots" or "googlebot" or "sculptflowbot")) continue;
            var content = meta.GetAttribute("content")?.ToLowerInvariant() ?? string.Empty;
            if (content.Contains("noindex") || content.Contains("none")) noindex = true;
            if (content.Contains("nofollow") || content.Contains("none")) nofollow = true;
        }
        return (noindex, nofollow);
    }

    private static bool LooksLikeContact(string line)
    {
        if (line.Contains('@') && line.Contains('.')) return true;
        var digits = line.Count(char.IsDigit);
        if (digits >= 7 && line.Length <= 90) return true;
        return ContactWords().IsMatch(line);
    }

    internal static string Clean(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : Whitespace().Replace(s, " ").Trim();

    private sealed class Walker
    {
        private readonly int _rootTextLength;
        private readonly IElement _root;
        private readonly StringBuilder _line = new();
        private bool _pendingListItem;

        public List<TextBlock> Blocks { get; } = new();

        public Walker(int rootTextLength, IElement root)
        {
            _rootTextLength = Math.Max(1, rootTextLength);
            _root = root;
        }

        public void WalkChildren(INode node)
        {
            foreach (var child in node.ChildNodes) Walk(child);
        }

        private void Walk(INode node)
        {
            switch (node)
            {
                case IText text:
                    _line.Append(text.Data).Append(' ');
                    return;
                case not IElement:
                    return;
            }

            var el = (IElement)node;
            if (ShouldSkip(el)) return;
            var tag = el.LocalName;

            switch (tag)
            {
                case "br": Flush(); return;
                case "img":
                    var alt = Clean(el.GetAttribute("alt"));
                    if (alt.Length >= 8) _line.Append(alt).Append(' ');
                    return;
                case "li":
                    Flush();
                    _pendingListItem = true;
                    WalkChildren(el);
                    Flush();
                    return;
                case "td" or "th":
                    WalkChildren(el);
                    _line.Append(" | ");
                    return;
                case "tr":
                    Flush();
                    WalkChildren(el);
                    var row = Clean(_line.ToString()).TrimEnd('|', ' ');
                    _line.Clear();
                    if (row.Length > 0) Blocks.Add(new TextBlock(row));
                    return;
            }

            if (BlockTags.Contains(tag))
            {
                Flush();
                WalkChildren(el);
                Flush();
            }
            else
            {
                WalkChildren(el);
            }
        }

        public void Flush()
        {
            var text = Clean(_line.ToString());
            _line.Clear();
            if (text.Length > 0) Blocks.Add(new TextBlock(text, _pendingListItem));
            _pendingListItem = false;
        }

        private bool ShouldSkip(IElement el)
        {
            var tag = el.LocalName;
            if (SkipTags.Contains(tag)) return true;
            if (el.HasAttribute("hidden")) return true;
            if (string.Equals(el.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase)) return true;

            var style = el.GetAttribute("style");
            if (style is not null && HiddenStyle().IsMatch(style)) return true;

            // <header> at page level is the site header/menu; inside an article/section it's the page heading — keep that.
            if (tag == "header" && !HasAncestor(el, "article", "main", "section")) return true;
            if (tag == "footer") return true; // contact lines are recovered separately by the extractor

            var role = el.GetAttribute("role");
            var noisyRole = role is not null && NoiseRoles.Contains(role);
            var noisyName = HasNoiseToken(el);
            if (!noisyRole && !noisyName) return false;

            // Guard against false positives such as <body class="has-sidebar">: never drop the element that
            // holds the page's headline or most of the text.
            if (ReferenceEquals(el, _root) || el.TagName == "BODY" || el.TagName == "MAIN") return false;
            if (el.QuerySelector("h1") is not null) return false;
            if (el.TextContent.Length > _rootTextLength * 0.6) return false;
            return true;
        }

        private static bool HasAncestor(IElement el, params string[] tags)
        {
            for (var p = el.ParentElement; p is not null; p = p.ParentElement)
                if (tags.Contains(p.LocalName)) return true;
            return false;
        }

        private static bool HasNoiseToken(IElement el)
        {
            var cls = el.GetAttribute("class");
            var id = el.GetAttribute("id");
            if (string.IsNullOrEmpty(cls) && string.IsNullOrEmpty(id)) return false;
            foreach (var token in Tokens().Split(($"{cls} {id}").ToLowerInvariant()))
            {
                if (token.Length > 0 && NoiseTokens.Contains(token)) return true;
            }
            return false;
        }
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex Tokens();
    [GeneratedRegex(@"display\s*:\s*none|visibility\s*:\s*hidden", RegexOptions.IgnoreCase)] private static partial Regex HiddenStyle();
    [GeneratedRegex(@"\b(address|tel|phone|call us|email|e-mail|hours|open|monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|fri|sat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ContactWords();
}
