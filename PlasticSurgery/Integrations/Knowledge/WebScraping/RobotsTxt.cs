using System.Text.RegularExpressions;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>
/// A small robots.txt evaluator: picks the group for our crawler's product token (falling back to "*"),
/// then applies the longest matching Allow/Disallow rule (Allow wins a tie), with "*" wildcards and a "$"
/// end anchor. Also exposes Crawl-delay. A missing/4xx robots.txt means "allow all"; an unreachable/5xx one
/// is treated by the crawler as "don't crawl" (see WebsiteScrapeProcessor).
/// </summary>
public sealed class RobotsTxt
{
    private readonly List<(bool Allow, Regex Pattern, int Length)> _rules = new();

    public double? CrawlDelaySeconds { get; private set; }

    public static RobotsTxt AllowAll { get; } = new();

    public bool IsAllowed(string pathAndQuery)
    {
        var path = string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;
        (bool Allow, int Length)? best = null;
        foreach (var (allow, pattern, length) in _rules)
        {
            if (!pattern.IsMatch(path)) continue;
            if (best is null || length > best.Value.Length || (length == best.Value.Length && allow))
            {
                best = (allow, length);
            }
        }
        return best?.Allow ?? true;
    }

    public static RobotsTxt Parse(string text, string productToken)
    {
        productToken = productToken.ToLowerInvariant();
        var specific = new Group();
        var wildcard = new Group();

        Group? current = null;
        var lastWasAgent = false;
        var currentIsSpecific = false;
        var currentIsWildcard = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (field == "user-agent")
            {
                if (!lastWasAgent) { currentIsSpecific = false; currentIsWildcard = false; }
                var agent = value.ToLowerInvariant();
                if (agent == "*") currentIsWildcard = true;
                else if (agent.Length > 0 && productToken.Contains(agent)) currentIsSpecific = true;
                lastWasAgent = true;
                current = currentIsSpecific ? specific : currentIsWildcard ? wildcard : null;
                continue;
            }

            lastWasAgent = false;
            if (current is null && !currentIsSpecific && !currentIsWildcard) continue;
            var target = currentIsSpecific ? specific : wildcard;
            if (!currentIsSpecific && !currentIsWildcard) continue;

            switch (field)
            {
                case "allow": target.Rules.Add((true, value)); break;
                case "disallow": target.Rules.Add((false, value)); break;
                case "crawl-delay":
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= 0)
                        target.CrawlDelay = d;
                    break;
            }
        }

        // A group naming us wins over "*"; otherwise the "*" group applies.
        var chosen = specific.HasContent ? specific : wildcard;
        var robots = new RobotsTxt { CrawlDelaySeconds = chosen.CrawlDelay };
        foreach (var (allow, pattern) in chosen.Rules)
        {
            if (pattern.Length == 0) continue; // "Disallow:" (empty) = allow everything
            robots._rules.Add((allow, ToRegex(pattern), pattern.Length));
        }
        return robots;
    }

    private static Regex ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        if (anchored) pattern = pattern[..^1];
        var body = Regex.Escape(pattern).Replace("\\*", ".*");
        return new Regex("^" + body + (anchored ? "$" : string.Empty), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private sealed class Group
    {
        public List<(bool Allow, string Pattern)> Rules { get; } = new();
        public double? CrawlDelay { get; set; }
        public bool HasContent => Rules.Count > 0 || CrawlDelay.HasValue;
    }
}
