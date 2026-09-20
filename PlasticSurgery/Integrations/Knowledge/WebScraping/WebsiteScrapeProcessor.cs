using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>Runs ONE crawl (a knowledge_website_scrape_runs row). Called by the background worker, never by a request.</summary>
public interface IWebsiteScrapeProcessor
{
    Task RunAsync(Guid runId, CancellationToken ct);
}

/// <summary>
/// The crawl engine. Three phases:
///
///  1. CRAWL   — breadth-first from the start URL, level by level with limited concurrency: robots.txt check,
///               SSRF-validated fetch, HTML extraction, link discovery (same site only, URL-normalized,
///               assets/traps filtered). Nothing is written to the Knowledge Base yet.
///  2. ANALYZE — with the whole site in memory: resolve canonical/redirect aliases, strip site-wide repeated
///               blocks (menus, footers…) while keeping them once on the start page, render + normalize each
///               page's text, SHA-256 it, and mark same-content pages as duplicates.
///  3. APPLY   — per page: unchanged (same hash) → nothing touched and NO embedding call; changed → the page's
///               existing knowledge_document is updated and only ITS chunks are rebuilt; new → a new document.
///               Documents are created/updated through IKnowledgeService (the existing chunking/embedding/
///               settings pipeline) — this class contains no vector or search logic. Pages that disappeared
///               are deactivated (never hard-deleted) once confidently gone.
///
/// One bad URL never fails the crawl: it is recorded on its page row and the crawl continues.
/// </summary>
public sealed class WebsiteScrapeProcessor : IWebsiteScrapeProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteFetchClient _fetch;
    private readonly IHtmlContentExtractor _extractor;
    private readonly SsrfGuard _guard;
    private readonly WebsiteScrapeOptions _options;
    private readonly ILogger<WebsiteScrapeProcessor> _logger;

    public WebsiteScrapeProcessor(
        ApplicationDbContext db, IKnowledgeService knowledge, IWebsiteFetchClient fetch, IHtmlContentExtractor extractor,
        SsrfGuard guard, WebsiteScrapeOptions options, ILogger<WebsiteScrapeProcessor> logger)
    {
        _db = db;
        _knowledge = knowledge;
        _fetch = fetch;
        _extractor = extractor;
        _guard = guard;
        _options = options;
        _logger = logger;
    }

    private enum Outcome { Pending, Failed, Skipped, Missing, Alias, NotModified, Content }

    /// <summary>In-memory state of one URL during a crawl.</summary>
    private sealed class CrawledPage
    {
        public string NormalizedUrl = string.Empty;
        public int Depth;
        public FetchResult? Fetch;
        public string? SkipReason;            // set before/without fetching (robots, document link)
        public string? RedirectTo;            // normalized final URL when the request redirected
        public ExtractedPage? Extracted;
        public string? Canonical;             // normalized + validated same-site canonical (≠ self), if any
        public List<string> InternalLinks = new();

        public Outcome Outcome;
        public string? Reason;
        public CrawledPage? AliasOf;          // redirect/canonical target (Outcome.Alias) or duplicate owner
        public bool IsDuplicate;
        public List<TextBlock>? Blocks;
        public string? Text;
        public string? Hash;
        public string Title = string.Empty;
    }

    private sealed class Counters
    {
        public int Indexed, New, Changed, Unchanged, Skipped, Duplicate, Failed, Removed;
    }

    public async Task RunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.KnowledgeWebsiteScrapeRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status != WebsiteScrapeStatus.Pending) return;

        var source = await _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.Id == run.WebsiteSourceId && s.ClinicId == run.ClinicId, ct);
        if (source is null)
        {
            run.Status = WebsiteScrapeStatus.Failed;
            run.ErrorSummary = "The website source no longer exists.";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        run.Status = WebsiteScrapeStatus.Crawling;
        run.StartedAt = DateTimeOffset.UtcNow;
        source.Status = WebsiteScrapeStatus.Crawling;
        await _db.SaveChangesAsync(ct);

        try
        {
            await CrawlAsync(run, source, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await FailAsync(run.Id, source.Id, "The crawl was interrupted because the application is shutting down. Start it again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Website crawl {RunId} failed unexpectedly.", run.Id);
            await FailAsync(run.Id, source.Id, "Unexpected error: " + ex.Message);
        }
    }

    // =====================================================================================================
    // Orchestration
    // =====================================================================================================
    private async Task CrawlAsync(KnowledgeWebsiteScrapeRun run, KnowledgeWebsiteSource source, CancellationToken ct)
    {
        var start = new Uri(source.NormalizedStartUrl);
        var notes = new List<string>();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(_options.MaxRunMinutes);

        try { await _guard.ValidateUrlAsync(start, ct); }
        catch (UnsafeUrlException ex) { await FinishAsync(run, source, WebsiteScrapeStatus.Failed, ex.Message); return; }

        var robotsResult = await _fetch.FetchRobotsAsync(start, ct);
        if (robotsResult.Unavailable)
        {
            await FinishAsync(run, source, WebsiteScrapeStatus.Failed,
                $"Couldn't read the site's robots.txt ({robotsResult.Note}), so the crawl was not started. Try again later.");
            return;
        }
        var robots = robotsResult.Robots ?? RobotsTxt.AllowAll;
        var delayMs = Math.Max(_options.PolitenessDelayMs, (int)Math.Min((robots.CrawlDelaySeconds ?? 0) * 1000, 5000));

        var existing = await _db.KnowledgeWebsitePages
            .Where(p => p.WebsiteSourceId == source.Id && p.ClinicId == source.ClinicId)
            .ToDictionaryAsync(p => p.NormalizedUrl, StringComparer.Ordinal, ct);

        // ------------------------------------------------------------------ 1. CRAWL
        var singlePage = source.CrawlMode == WebsiteCrawlMode.SinglePage;
        var origin = start;                       // effective origin (scheme+host) — updated if the start URL redirects to www/https
        var pages = new Dictionary<string, CrawledPage>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { source.NormalizedStartUrl };
        var pathVariants = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var truncated = false;
        var documentLinks = 0;
        var startPageKey = source.NormalizedStartUrl;
        var frontier = new List<(string Url, int Depth)> { (source.NormalizedStartUrl, 0) };

        for (var level = 0; frontier.Count > 0; level++)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                truncated = true;
                notes.Add($"The {_options.MaxRunMinutes}-minute crawl time limit was reached.");
                break;
            }

            var results = await FetchLevelAsync(frontier, robots, delayMs, existing, start, ct);
            var next = new List<(string Url, int Depth)>();

            foreach (var cp in results)
            {
                pages[cp.NormalizedUrl] = cp;
                if (cp.SkipReason is not null || cp.Fetch is null) continue;

                if (cp.Fetch.NotModified)
                {
                    // Unchanged on the server: reuse the links we stored last time so the crawl can still go through it.
                    if (existing.TryGetValue(cp.NormalizedUrl, out var prior) && prior.Links is not null && !singlePage)
                    {
                        var stored = JsonSerializer.Deserialize<List<string>>(prior.Links) ?? new();
                        CollectLinks(cp, stored, origin, level, seen, next, pathVariants, ref truncated, ref documentLinks, pages);
                    }
                    continue;
                }
                if (cp.Extracted is null) continue;

                if (Uri.TryCreate(cp.Fetch.FinalUrl, UriKind.Absolute, out var finalUri))
                {
                    if (level == 0 && ReferenceEquals(cp, results[0]) && UrlNormalizer.IsSameSite(finalUri, start))
                    {
                        origin = new Uri(finalUri.GetLeftPart(UriPartial.Authority));
                    }

                    var finalNorm = UrlNormalizer.Normalize(cp.Fetch.FinalUrl);
                    finalNorm = finalNorm is null ? null : UrlNormalizer.ToOrigin(finalNorm, origin);
                    if (finalNorm is not null && finalNorm != cp.NormalizedUrl)
                    {
                        // The request redirected: the requested URL becomes an alias of where it landed. Register the
                        // landing page with the content we already have (no second fetch) if we haven't seen it.
                        cp.RedirectTo = finalNorm;
                        if (cp.NormalizedUrl == startPageKey) startPageKey = finalNorm;
                        if (seen.Add(finalNorm))
                        {
                            var target = new CrawledPage { NormalizedUrl = finalNorm, Depth = cp.Depth, Fetch = cp.Fetch, Extracted = cp.Extracted };
                            pages[finalNorm] = target;
                            if (!singlePage) CollectLinks(target, target.Extracted!.Links, origin, level, seen, next, pathVariants, ref truncated, ref documentLinks, pages);
                        }
                        continue;
                    }
                }

                if (!singlePage) CollectLinks(cp, cp.Extracted.Links, origin, level, seen, next, pathVariants, ref truncated, ref documentLinks, pages);
            }

            run.PagesDiscovered = seen.Count;
            run.PagesProcessed = pages.Count;
            await _db.SaveChangesAsync(ct);

            if (singlePage) break;
            frontier = next;
        }

        // ------------------------------------------------------------------ 2. ANALYZE
        var boilerplate = Analyze(pages.Values.ToList(), existing, source, startPageKey, origin, out var newBoilerplate);

        // ------------------------------------------------------------------ 3. APPLY
        await _db.Entry(source).ReloadAsync(ct); // pick up a deactivation that happened while we were crawling
        var counters = new Counters();
        await ApplyAsync(run, source, pages, existing, counters, ct);

        // Pages we knew about that this crawl never reached.
        // If the starting page itself couldn't be used (unreachable, 404, blocked by robots, not HTML, noindex)
        // the whole crawl is reported as failed — and nothing is treated as "missing".
        var startFailed = pages.TryGetValue(source.NormalizedStartUrl, out var startPage)
                          && startPage.Outcome is Outcome.Failed or Outcome.Missing or Outcome.Skipped;
        if (truncated)
        {
            notes.Add($"Crawl limits reached (max {_options.MaxPages} pages, depth {_options.MaxDepth}) — some pages were not visited, so no missing pages were removed.");
        }
        else if (!startFailed)
        {
            foreach (var row in existing.Values.Where(r => !pages.ContainsKey(r.NormalizedUrl) && r.Status != WebsitePageStatus.Removed))
            {
                row.MissingCount++;
                if (row.MissingCount >= 2)
                {
                    row.Status = WebsitePageStatus.Removed;
                    row.RemovedAt = DateTimeOffset.UtcNow;
                    row.FailureReason = "No longer linked from the website.";
                    if (row.KnowledgeDocumentId is { } docId) await _knowledge.SetActiveAsync(source.ClinicId, docId, false, ct);
                    counters.Removed++;
                }
                else
                {
                    row.FailureReason = "Not found on the last crawl — will be removed if it is still missing next time.";
                }
            }
        }

        // ------------------------------------------------------------------ finish
        if (newBoilerplate is not null) source.BoilerplateBlockHashes = JsonSerializer.Serialize(newBoilerplate);

        // Failed/missing pages — plus the start page's own reason when it was skipped (robots.txt, noindex, not HTML),
        // since that is exactly why nothing was imported.
        var failures = pages.Values.Where(p => p.Outcome is Outcome.Failed or Outcome.Missing
                                               || (startFailed && p.NormalizedUrl == source.NormalizedStartUrl))
            .Select(p => $"{p.NormalizedUrl}: {p.Reason}").Take(10).ToList();
        var summary = string.Join(" ", notes.Concat(failures.Count > 0 ? new[] { "Problems — " + string.Join(" | ", failures) } : Array.Empty<string>()));

        var status = startFailed ? WebsiteScrapeStatus.Failed
            : counters.Failed > 0 ? WebsiteScrapeStatus.CompletedWithErrors
            : WebsiteScrapeStatus.Completed;

        run.PagesDiscovered = seen.Count;
        run.PagesProcessed = pages.Count;
        run.PagesIndexed = counters.Indexed;
        run.PagesNew = counters.New;
        run.PagesChanged = counters.Changed;
        run.PagesUnchanged = counters.Unchanged;
        run.PagesSkipped = counters.Skipped;
        run.PagesDuplicate = counters.Duplicate;
        run.PagesFailed = counters.Failed;
        run.PagesRemoved = counters.Removed;
        await FinishAsync(run, source, status, string.IsNullOrWhiteSpace(summary) ? null : summary);
    }

    // =====================================================================================================
    // Phase 1 helpers
    // =====================================================================================================
    private async Task<List<CrawledPage>> FetchLevelAsync(
        List<(string Url, int Depth)> frontier, RobotsTxt robots, int delayMs,
        Dictionary<string, KnowledgeWebsitePage> existing, Uri start, CancellationToken ct)
    {
        var results = new CrawledPage[frontier.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, frontier.Count),
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = ct },
            async (i, token) => results[i] = await FetchOneAsync(frontier[i].Url, frontier[i].Depth, robots, delayMs, existing, start, token));
        return results.ToList();
    }

    private async Task<CrawledPage> FetchOneAsync(
        string url, int depth, RobotsTxt robots, int delayMs, Dictionary<string, KnowledgeWebsitePage> existing, Uri start, CancellationToken ct)
    {
        var cp = new CrawledPage { NormalizedUrl = url, Depth = depth };
        var uri = new Uri(url);

        if (!robots.IsAllowed(uri.PathAndQuery))
        {
            cp.SkipReason = "Blocked by the website's robots.txt.";
            return cp;
        }

        if (delayMs > 0) await Task.Delay(delayMs, ct);

        // Conditional request only when we still hold the document it produced (a 304 gives us no body).
        existing.TryGetValue(url, out var prior);
        var conditional = prior is { KnowledgeDocumentId: not null, ContentHash: not null };
        cp.Fetch = await _fetch.FetchPageAsync(
            uri, conditional ? prior!.ETag : null, conditional ? prior!.LastModifiedHeader : null,
            next => UrlNormalizer.IsSameSite(next, start), ct);

        if (cp.Fetch.Html is not null && Uri.TryCreate(cp.Fetch.FinalUrl, UriKind.Absolute, out var finalUri))
        {
            try
            {
                cp.Extracted = _extractor.Extract(cp.Fetch.Html, finalUri);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTML extraction failed for {Url}", url);
                cp.Fetch = cp.Fetch with { Html = null, ErrorKind = FetchErrorKind.HttpError, Error = "The page's HTML could not be parsed." };
            }
        }
        return cp;
    }

    /// <summary>Normalizes a page's links, keeps same-site page links, and queues the ones not yet seen.</summary>
    private void CollectLinks(
        CrawledPage page, IEnumerable<string> hrefs, Uri origin, int level, HashSet<string> seen,
        List<(string Url, int Depth)> next, Dictionary<string, HashSet<string>> pathVariants,
        ref bool truncated, ref int documentLinks, Dictionary<string, CrawledPage> pages)
    {
        var candidates = new List<string>(hrefs);
        if (page.Extracted?.CanonicalHref is { } canonicalHref) candidates.Add(canonicalHref);

        foreach (var href in candidates)
        {
            var norm = UrlNormalizer.Normalize(href);
            if (norm is null) continue;
            norm = UrlNormalizer.ToOrigin(norm, origin);
            if (!Uri.TryCreate(norm, UriKind.Absolute, out var u) || !UrlNormalizer.IsSameSite(u, origin)) continue; // external: never crawled
            if (UrlNormalizer.IsAssetUrl(norm)) continue;                                                                       // images/css/js/media: never crawled or stored

            if (UrlNormalizer.IsDocumentUrl(norm))
            {
                // PDFs/Office files: not crawled here (Upload Document handles them) — but tell staff they exist.
                if (documentLinks < 20 && seen.Add(norm))
                {
                    documentLinks++;
                    pages[norm] = new CrawledPage { NormalizedUrl = norm, Depth = level + 1, SkipReason = "A document file — not crawled. Use \"Upload Document\" to add it." };
                }
                continue;
            }

            if (LooksLikeCrawlTrap(u, pathVariants)) continue;

            if (page.InternalLinks.Count < _options.MaxLinksPerPage && !page.InternalLinks.Contains(norm)) page.InternalLinks.Add(norm);
            if (seen.Contains(norm)) continue;
            if (level + 1 > _options.MaxDepth || seen.Count >= _options.MaxPages) { truncated = true; continue; }
            seen.Add(norm);
            next.Add((norm, level + 1));
        }
    }

    private static readonly string[] TrapPathParts =
    {
        "wp-admin", "wp-login.php", "wp-json", "xmlrpc.php", "cart", "checkout", "my-account", "login", "logout",
        "register", "feed", "trackback", "calendar", "ical"
    };

    private static readonly HashSet<string> TrapQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "replytocom", "share", "print", "action", "add-to-cart", "sort", "orderby", "order", "filter", "filters", "view",
        "sessionid", "phpsessid", "sid", "format", "ical", "outlook-ical", "tribe-bar-date", "date", "month", "year", "day"
    };

    /// <summary>Avoids infinite spaces: calendars, faceted filters, session ids, endlessly nested/repeating paths,
    /// and too many query-string variants of one path.</summary>
    private bool LooksLikeCrawlTrap(Uri u, Dictionary<string, HashSet<string>> pathVariants)
    {
        var segments = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 10) return true;
        if (segments.GroupBy(s => s.ToLowerInvariant()).Any(g => g.Count() >= 3)) return true;
        if (segments.Any(s => TrapPathParts.Contains(s.ToLowerInvariant()))) return true;

        var query = u.Query.TrimStart('?');
        if (query.Length == 0) return false;
        var keys = query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')[0]).ToList();
        if (keys.Count > 4 || keys.Any(k => TrapQueryKeys.Contains(k))) return true;

        var path = u.GetLeftPart(UriPartial.Path);
        if (!pathVariants.TryGetValue(path, out var set)) pathVariants[path] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(query);
        return set.Count > _options.MaxQueryVariantsPerPath;
    }

    // =====================================================================================================
    // Phase 2: analysis
    // =====================================================================================================
    /// <summary>Classifies every page, resolves canonical aliases, strips site-wide boilerplate, renders + hashes the
    /// text and finds duplicate content. Returns the boilerplate hash set to remember (null if unchanged).</summary>
    private HashSet<string> Analyze(
        List<CrawledPage> all, Dictionary<string, KnowledgeWebsitePage> existing, KnowledgeWebsiteSource source,
        string startPageKey, Uri origin, out HashSet<string>? newBoilerplate)
    {
        newBoilerplate = null;
        var byUrl = all.ToDictionary(p => p.NormalizedUrl, StringComparer.Ordinal);

        // 1. Classify what happened when we tried to fetch each page.
        foreach (var cp in all)
        {
            var f = cp.Fetch;
            if (cp.SkipReason is not null) { cp.Outcome = Outcome.Skipped; cp.Reason = cp.SkipReason; continue; }
            if (f is null) { cp.Outcome = Outcome.Failed; cp.Reason = "Not fetched."; continue; }

            if (f.NotModified) { cp.Outcome = Outcome.NotModified; continue; }

            if (!f.Ok)
            {
                cp.Reason = f.Error;
                cp.Outcome = f.ErrorKind switch
                {
                    FetchErrorKind.NotHtml or FetchErrorKind.ExternalRedirect => Outcome.Skipped,
                    FetchErrorKind.HttpError when f.StatusCode is 404 or 410 => Outcome.Missing,
                    _ => Outcome.Failed
                };
                continue;
            }

            if (cp.RedirectTo is not null)
            {
                cp.Outcome = Outcome.Alias;
                cp.Reason = $"Redirects to {cp.RedirectTo}.";
                cp.AliasOf = byUrl.GetValueOrDefault(cp.RedirectTo);
                continue;
            }

            if (cp.Extracted is null) { cp.Outcome = Outcome.Failed; cp.Reason = "No content."; continue; }
            if (cp.Extracted.NoIndex) { cp.Outcome = Outcome.Skipped; cp.Reason = "The page asks not to be indexed (noindex)."; continue; }

            cp.Outcome = Outcome.Content;
            cp.Canonical = ResolveCanonical(cp, origin);
        }

        // 2. Canonical aliases: /procedure?utm=… whose canonical is /procedure → not a page of its own.
        foreach (var cp in all.Where(p => p.Outcome == Outcome.Content && p.Canonical is not null))
        {
            var target = FollowCanonical(cp, byUrl);
            if (target is not null && target != cp)
            {
                cp.Outcome = Outcome.Alias;
                cp.AliasOf = target;
                cp.Reason = $"Canonical URL points to {target.NormalizedUrl}.";
            }
        }

        // 3. Site-wide boilerplate (menus, footers, banners): blocks repeated across many pages.
        var content = all.Where(p => p.Outcome == Outcome.Content).ToList();
        foreach (var cp in content)
        {
            cp.Blocks = cp.Extracted!.Blocks.ToList();
            cp.Title = cp.Extracted.Title ?? string.Empty;
        }

        var boilerplate = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(source.BoilerplateBlockHashes))
        {
            try { foreach (var h in JsonSerializer.Deserialize<List<string>>(source.BoilerplateBlockHashes) ?? new()) boilerplate.Add(h); }
            catch (JsonException) { /* ignore a corrupt value */ }
        }

        if (content.Count >= 4)
        {
            var threshold = Math.Max(3, (int)Math.Ceiling(content.Count * 0.5));
            var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cp in content)
            {
                foreach (var key in cp.Blocks!.Select(b => BlockKey(b.Text)).Distinct()) frequency[key] = frequency.GetValueOrDefault(key) + 1;
            }
            foreach (var (key, count) in frequency) if (count >= threshold) boilerplate.Add(key);
            newBoilerplate = boilerplate.Count > 3000 ? boilerplate.Take(3000).ToHashSet(StringComparer.Ordinal) : boilerplate;
        }

        foreach (var cp in content)
        {
            // Repeated blocks are removed everywhere EXCEPT the start page, so contact details / hours that
            // appear in every footer still exist once in the Knowledge Base.
            if (boilerplate.Count > 0 && cp.NormalizedUrl != startPageKey)
            {
                cp.Blocks = cp.Blocks!.Where(b => !boilerplate.Contains(BlockKey(b.Text))).ToList();
            }

            cp.Text = KnowledgeTextNormalizer.Normalize(Render(cp.Blocks!));
            if (cp.Text.Length < _options.MinTextChars)
            {
                cp.Outcome = Outcome.Skipped;
                cp.Reason = cp.Text.Length == 0
                    ? "No readable text found (the page may be built with JavaScript, which isn't supported)."
                    : $"Too little text ({cp.Text.Length} characters).";
                continue;
            }
            if (cp.Text.Length > _options.MaxTextChars)
            {
                cp.Outcome = Outcome.Skipped;
                cp.Reason = $"Too much text ({cp.Text.Length:N0} characters; limit {_options.MaxTextChars:N0}).";
                continue;
            }
            cp.Hash = Sha256(cp.Text.Normalize(NormalizationForm.FormC));
            cp.Title = ChooseTitle(cp);
        }

        // 4. Duplicate content within this website: keep ONE owner per hash. A page that already owns the hash
        //    (indexed before, document still there) stays the owner; then canonical/shortest/shallowest wins.
        var owners = new Dictionary<string, CrawledPage?>(StringComparer.Ordinal);
        var crawledUrls = new HashSet<string>(all.Select(p => p.NormalizedUrl), StringComparer.Ordinal);
        var externalOwners = existing.Values
            .Where(r => !crawledUrls.Contains(r.NormalizedUrl) && r.ContentHash is not null && r.KnowledgeDocumentId is not null
                        && r.Status is WebsitePageStatus.Indexed or WebsitePageStatus.Unchanged)
            .Select(r => r.ContentHash!).ToHashSet(StringComparer.Ordinal);

        var candidates = all.Where(p => p.Outcome == Outcome.Content && p.Hash is not null)
            .OrderByDescending(p => existing.TryGetValue(p.NormalizedUrl, out var r) && r.ContentHash == p.Hash && r.KnowledgeDocumentId is not null)
            .ThenByDescending(p => p.Canonical is null)
            .ThenBy(p => p.Depth)
            .ThenBy(p => p.NormalizedUrl.Length)
            .ThenBy(p => p.NormalizedUrl, StringComparer.Ordinal)
            .ToList();

        foreach (var cp in candidates)
        {
            if (owners.TryGetValue(cp.Hash!, out var owner) && owner is not null)
            {
                cp.IsDuplicate = true;
                cp.AliasOf = owner;
                cp.Reason = $"Same content as {owner.NormalizedUrl}.";
            }
            else if (externalOwners.Contains(cp.Hash!))
            {
                cp.IsDuplicate = true;
                cp.Reason = "Same content as another indexed page of this website.";
            }
            else
            {
                owners[cp.Hash!] = cp;
            }
        }

        return boilerplate;
    }

    private static string? ResolveCanonical(CrawledPage cp, Uri origin)
    {
        var href = cp.Extracted?.CanonicalHref;
        if (string.IsNullOrWhiteSpace(href)) return null;
        var norm = UrlNormalizer.Normalize(href);
        if (norm is null) return null;
        norm = UrlNormalizer.ToOrigin(norm, origin);
        if (!Uri.TryCreate(norm, UriKind.Absolute, out var u)) return null;

        // Conservative: never trust a canonical on another site, and ignore the common misconfiguration
        // where every page claims the home page as its canonical.
        if (!UrlNormalizer.IsSameSite(u, origin)) return null;
        if (norm == cp.NormalizedUrl) return null;
        if ((u.AbsolutePath == "/" || u.AbsolutePath.Length == 0) && new Uri(cp.NormalizedUrl).AbsolutePath != "/") return null;
        return norm;
    }

    private static CrawledPage? FollowCanonical(CrawledPage cp, Dictionary<string, CrawledPage> byUrl)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { cp.NormalizedUrl };
        var current = cp;
        while (current.Canonical is not null)
        {
            if (!visited.Add(current.Canonical)) return null;                 // canonical loop → index the page itself
            if (!byUrl.TryGetValue(current.Canonical, out var target)) return null;
            if (target.Outcome != Outcome.Content) return null;               // target wasn't usable → index the page itself
            if (target.Canonical is null) return target;
            current = target;
        }
        return current == cp ? null : current;
    }

    private static string ChooseTitle(CrawledPage cp)
    {
        var title = HtmlContentExtractor.Clean(cp.Title);
        if (title.Length == 0)
        {
            var uri = new Uri(cp.NormalizedUrl);
            var last = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            title = string.IsNullOrEmpty(last) ? uri.Host : Uri.UnescapeDataString(last).Replace('-', ' ').Replace('_', ' ');
        }
        return title.Length > 200 ? title[..200] : title;
    }

    private static string Render(IEnumerable<TextBlock> blocks)
    {
        var sb = new StringBuilder();
        TextBlock? previous = null;
        foreach (var block in blocks)
        {
            if (previous is not null) sb.Append(previous.ListItem && block.ListItem ? "\n" : "\n\n");
            sb.Append(block.ListItem ? "- " : string.Empty).Append(block.Text);
            previous = block;
        }
        return sb.ToString();
    }

    private static string BlockKey(string text) => Sha256(text.Trim().ToLowerInvariant())[..16];

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // =====================================================================================================
    // Phase 3: apply to page rows + the Knowledge Base
    // =====================================================================================================
    private async Task ApplyAsync(
        KnowledgeWebsiteScrapeRun run, KnowledgeWebsiteSource source, Dictionary<string, CrawledPage> pages,
        Dictionary<string, KnowledgeWebsitePage> existing, Counters counters, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new Dictionary<string, KnowledgeWebsitePage>(StringComparer.Ordinal);

        KnowledgeWebsitePage RowFor(CrawledPage cp)
        {
            if (rows.TryGetValue(cp.NormalizedUrl, out var known)) return known;
            if (!existing.TryGetValue(cp.NormalizedUrl, out var row))
            {
                row = new KnowledgeWebsitePage
                {
                    Id = Guid.NewGuid(),
                    ClinicId = source.ClinicId,
                    WebsiteSourceId = source.Id,
                    NormalizedUrl = cp.NormalizedUrl,
                    Url = cp.NormalizedUrl,
                    Depth = cp.Depth,
                    FirstDiscoveredAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.KnowledgeWebsitePages.Add(row);
                existing[cp.NormalizedUrl] = row;
            }
            rows[cp.NormalizedUrl] = row;
            return row;
        }

        // Owners first so alias/duplicate rows can point at their row ids.
        var ordered = pages.Values.OrderBy(p => p.Outcome == Outcome.Content && !p.IsDuplicate ? 0 : 1).ThenBy(p => p.Depth).ToList();
        foreach (var cp in ordered) RowFor(cp);

        foreach (var cp in ordered)
        {
            ct.ThrowIfCancellationRequested();
            var row = RowFor(cp);
            var f = cp.Fetch;

            row.LastSeenAt = now;
            if (f is not null) { row.HttpStatus = f.StatusCode; row.ContentType = f.ContentType; row.LastScrapedAt = now; }
            if (cp.InternalLinks.Count > 0) row.Links = JsonSerializer.Serialize(cp.InternalLinks);
            if (cp.Outcome != Outcome.Missing) row.MissingCount = 0;
            row.DuplicateOfPageId = null;

            switch (cp.Outcome)
            {
                case Outcome.Failed:
                    row.Status = WebsitePageStatus.Failed;
                    row.FailureReason = cp.Reason;
                    counters.Failed++;
                    break;

                case Outcome.Skipped:
                    row.Status = WebsitePageStatus.Skipped;
                    row.FailureReason = cp.Reason;
                    await DropDocumentAsync(source, row, ct);
                    counters.Skipped++;
                    break;

                case Outcome.Missing:
                    row.MissingCount++;
                    row.FailureReason = cp.Reason;
                    if (row.KnowledgeDocumentId is { } gone && (f?.StatusCode == 410 || row.MissingCount >= 2))
                    {
                        // Confidently gone (410, or 404 on two crawls): stop it being searchable, keep the history.
                        row.Status = WebsitePageStatus.Removed;
                        row.RemovedAt = now;
                        await _knowledge.SetActiveAsync(source.ClinicId, gone, false, ct);
                        counters.Removed++;
                    }
                    else
                    {
                        row.Status = WebsitePageStatus.Failed;
                        if (row.KnowledgeDocumentId is not null) row.FailureReason += " — it will be removed if it is still missing next crawl.";
                        counters.Failed++;
                    }
                    break;

                case Outcome.Alias:
                    row.Status = WebsitePageStatus.Duplicate;
                    row.FailureReason = cp.Reason;
                    row.CanonicalUrl = cp.Canonical ?? cp.RedirectTo;
                    row.DuplicateOfPageId = cp.AliasOf is null ? null : RowFor(cp.AliasOf).Id;
                    await DropDocumentAsync(source, row, ct);
                    counters.Duplicate++;
                    break;

                case Outcome.NotModified:
                    if (row.KnowledgeDocumentId is null)
                    {
                        // Nothing to keep "unchanged" (its document was deleted) — force a full fetch next time.
                        row.ETag = null; row.LastModifiedHeader = null; row.ContentHash = null;
                    }
                    else if (row.Status == WebsitePageStatus.Removed && source.IsActive)
                    {
                        await _knowledge.SetActiveAsync(source.ClinicId, row.KnowledgeDocumentId.Value, true, ct);
                    }
                    row.Status = WebsitePageStatus.Unchanged;
                    row.FailureReason = null;
                    row.RemovedAt = null;
                    counters.Unchanged++;
                    break;

                case Outcome.Content:
                    row.Title = cp.Title;
                    row.CanonicalUrl = cp.Canonical;
                    row.ETag = f!.ETag;
                    row.LastModifiedHeader = f.LastModified;

                    if (cp.IsDuplicate)
                    {
                        row.Status = WebsitePageStatus.Duplicate;
                        row.FailureReason = cp.Reason;
                        row.ContentHash = cp.Hash;
                        row.DuplicateOfPageId = cp.AliasOf is null ? null : RowFor(cp.AliasOf).Id;
                        await DropDocumentAsync(source, row, ct);
                        counters.Duplicate++;
                        break;
                    }

                    await IndexPageAsync(source, cp, row, counters, ct);
                    break;
            }

            row.UpdatedAt = now;

            // Keep the run's counters live while pages are applied, so the status page shows real progress.
            run.PagesProcessed = pages.Count;
            run.PagesIndexed = counters.Indexed;
            run.PagesNew = counters.New;
            run.PagesChanged = counters.Changed;
            run.PagesUnchanged = counters.Unchanged;
            run.PagesSkipped = counters.Skipped;
            run.PagesDuplicate = counters.Duplicate;
            run.PagesFailed = counters.Failed;
            run.PagesRemoved = counters.Removed;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task IndexPageAsync(KnowledgeWebsiteSource source, CrawledPage cp, KnowledgeWebsitePage row, Counters counters, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var wasRemoved = row.Status == WebsitePageStatus.Removed;
        try
        {
            // UNCHANGED: same normalized URL + same content hash + the document still exists → touch nothing.
            // No document rewrite, no chunk delete/recreate, NO embedding request.
            if (row.ContentHash == cp.Hash && row.KnowledgeDocumentId is { } sameDoc
                && await _db.KnowledgeDocuments.AnyAsync(d => d.Id == sameDoc && d.ClinicId == source.ClinicId, ct))
            {
                if (wasRemoved && source.IsActive)
                {
                    await _knowledge.SetActiveAsync(source.ClinicId, sameDoc, true, ct);
                }
                row.Status = WebsitePageStatus.Unchanged;
                row.FailureReason = null;
                row.RemovedAt = null;
                counters.Unchanged++;
                return;
            }

            KnowledgeDocumentResponse? doc = null;
            var changed = false;
            if (row.KnowledgeDocumentId is { } docId)
            {
                // CHANGED: update the existing document and rebuild ONLY its chunks/embeddings.
                doc = await _knowledge.ReplaceSourceContentAsync(source.ClinicId, docId, cp.Title, cp.Text!, ct);
                changed = doc is not null;
            }
            doc ??= await _knowledge.CreateFromSourceAsync(source.ClinicId, new ExternalKnowledgeDocument(
                KnowledgeSourceType.Website, cp.Title, source.Category, cp.Text!, source.IsActive, cp.NormalizedUrl), ct);

            row.KnowledgeDocumentId = doc.Id;
            row.ContentHash = cp.Hash;
            row.Status = WebsitePageStatus.Indexed;
            row.FailureReason = null;
            row.RemovedAt = null;
            counters.Indexed++;
            if (changed) counters.Changed++; else counters.New++;

            // A document deactivated because the page vanished comes back with the page — but a document staff
            // deactivated by hand stays as they left it.
            if (wasRemoved && source.IsActive && !doc.IsActive) await _knowledge.SetActiveAsync(source.ClinicId, doc.Id, true, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Typically the embedding provider being unavailable. The hash is NOT recorded, so the next crawl retries.
            _logger.LogWarning("Indexing {Url} failed: {Message}", cp.NormalizedUrl, ex.Message);
            row.Status = WebsitePageStatus.Failed;
            row.FailureReason = "Couldn't index this page: " + ex.Message;
            counters.Failed++;
        }
        row.UpdatedAt = now;
    }

    /// <summary>A page that is no longer indexable (skipped/alias/duplicate) must not keep a searchable document.</summary>
    private async Task DropDocumentAsync(KnowledgeWebsiteSource source, KnowledgeWebsitePage row, CancellationToken ct)
    {
        if (row.KnowledgeDocumentId is { } docId)
        {
            await _knowledge.DeleteAsync(source.ClinicId, docId, ct);
            row.KnowledgeDocumentId = null;
        }
        row.ContentHash = null;
    }

    // =====================================================================================================
    // Finishing
    // =====================================================================================================
    private async Task FinishAsync(KnowledgeWebsiteScrapeRun run, KnowledgeWebsiteSource source, string status, string? summary)
    {
        var now = DateTimeOffset.UtcNow;
        run.Status = status;
        run.CompletedAt = now;
        run.ErrorSummary = summary;
        source.Status = status;
        if (status != WebsiteScrapeStatus.Failed) source.LastScrapedAt = now;
        await _db.SaveChangesAsync();
    }

    /// <summary>Failure path used when the tracked state may be inconsistent: start from a clean slate.</summary>
    private async Task FailAsync(Guid runId, Guid sourceId, string message)
    {
        _db.ChangeTracker.Clear();
        var run = await _db.KnowledgeWebsiteScrapeRuns.FirstOrDefaultAsync(r => r.Id == runId);
        var source = await _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.Id == sourceId);
        if (run is not null) { run.Status = WebsiteScrapeStatus.Failed; run.CompletedAt = DateTimeOffset.UtcNow; run.ErrorSummary = message; }
        if (source is not null) source.Status = WebsiteScrapeStatus.Failed;
        await _db.SaveChangesAsync();
    }
}
