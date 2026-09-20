namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>
/// Safety limits and behaviour for the website crawler (config section "Knowledge:WebScraping"). Every
/// value is clamped by <see cref="Resolve"/> so a bad config can't produce an unbounded crawl.
/// </summary>
public class WebsiteScrapeOptions
{
    public const string SectionName = "Knowledge:WebScraping";

    /// <summary>Most distinct URLs one crawl will visit (hard ceiling 500).</summary>
    public int MaxPages { get; set; } = 100;
    /// <summary>Link depth from the start URL (start page = 0). Hard ceiling 6.</summary>
    public int MaxDepth { get; set; } = 3;
    /// <summary>Simultaneous requests to the site. Hard ceiling 6.</summary>
    public int MaxConcurrency { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 15;
    /// <summary>Largest HTML response we will read (after decompression).</summary>
    public int MaxResponseBytes { get; set; } = 2_000_000;
    public int MaxRedirects { get; set; } = 5;
    /// <summary>Minimum pause before each request (politeness); robots.txt Crawl-delay can raise it (capped at 5 s).</summary>
    public int PolitenessDelayMs { get; set; } = 300;
    /// <summary>Wall-clock ceiling for one crawl.</summary>
    public int MaxRunMinutes { get; set; } = 20;
    /// <summary>Pages with less readable text than this are skipped (often JavaScript-rendered shells).</summary>
    public int MinTextChars { get; set; } = 80;
    public int MaxTextChars { get; set; } = 200_000;
    /// <summary>Different query strings tolerated for one path — stops filter/calendar/pagination traps.</summary>
    public int MaxQueryVariantsPerPath { get; set; } = 5;
    public int MaxLinksPerPage { get; set; } = 300;
    /// <summary>How many websites one clinic may configure.</summary>
    public int MaxSourcesPerClinic { get; set; } = 25;

    public string UserAgent { get; set; } = "SculptFlowBot/1.0 (+https://sculptflowapp.onrender.com; clinic knowledge-base crawler)";

    /// <summary>TESTING ONLY, and honoured ONLY when the app runs in the Development environment: exact
    /// "host:port" entries that bypass the private-address/port SSRF rules (e.g. a local fake site at
    /// "127.0.0.1:5098"). In Production this list is ignored entirely.</summary>
    public string[] DevAllowedHosts { get; set; } = Array.Empty<string>();

    public static WebsiteScrapeOptions Resolve(IConfiguration configuration)
    {
        var o = configuration.GetSection(SectionName).Get<WebsiteScrapeOptions>() ?? new WebsiteScrapeOptions();
        o.MaxPages = Math.Clamp(o.MaxPages, 1, 500);
        o.MaxDepth = Math.Clamp(o.MaxDepth, 0, 6);
        o.MaxConcurrency = Math.Clamp(o.MaxConcurrency, 1, 6);
        o.RequestTimeoutSeconds = Math.Clamp(o.RequestTimeoutSeconds, 2, 60);
        o.MaxResponseBytes = Math.Clamp(o.MaxResponseBytes, 50_000, 10_000_000);
        o.MaxRedirects = Math.Clamp(o.MaxRedirects, 0, 10);
        o.PolitenessDelayMs = Math.Clamp(o.PolitenessDelayMs, 0, 5_000);
        o.MaxRunMinutes = Math.Clamp(o.MaxRunMinutes, 1, 120);
        o.MinTextChars = Math.Clamp(o.MinTextChars, 1, 5_000);
        o.MaxTextChars = Math.Clamp(o.MaxTextChars, 1_000, 1_000_000);
        o.MaxQueryVariantsPerPath = Math.Clamp(o.MaxQueryVariantsPerPath, 1, 50);
        o.MaxLinksPerPage = Math.Clamp(o.MaxLinksPerPage, 10, 2_000);
        o.MaxSourcesPerClinic = Math.Clamp(o.MaxSourcesPerClinic, 1, 200);
        o.DevAllowedHosts ??= Array.Empty<string>();
        return o;
    }
}
