using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Engines.WebScraping;

namespace PlasticSurgery.Tests.Engines;

public class WebsiteScrapeProcessorTests
{
    private const string Root = "https://8.8.8.8";
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IWebsiteSourceRepository> _websites = new();
    private readonly Mock<IKnowledgeDocumentRepository> _documents = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IWebsiteFetchClient> _fetch = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Dictionary<string, FetchResult> _site = new();
    private readonly Dictionary<string, KnowledgeWebsitePage> _existing = new();
    private readonly List<KnowledgeWebsitePage> _addedPages = new();
    private readonly List<(string Title, string Content, string? Url)> _created = new();
    private readonly List<(Guid Id, string Content)> _replaced = new();
    private readonly List<Guid> _deleted = new();
    private readonly List<(Guid Id, bool Active)> _toggled = new();
    private KnowledgeWebsiteSource _source;
    private KnowledgeWebsiteScrapeRun _run;
    private RobotsFetchResult _robots = new(RobotsTxt.AllowAll, false, null);

    public WebsiteScrapeProcessorTests()
    {
        _config.SetupGet(c => c.WebScrapingMaxRunMinutes).Returns(5);
        _config.SetupGet(c => c.WebScrapingPolitenessDelayMs).Returns(0);
        _config.SetupGet(c => c.WebScrapingMaxConcurrency).Returns(2);
        _config.SetupGet(c => c.WebScrapingMaxDepth).Returns(3);
        _config.SetupGet(c => c.WebScrapingMaxPages).Returns(20);
        _config.SetupGet(c => c.WebScrapingMinTextChars).Returns(50);
        _config.SetupGet(c => c.WebScrapingMaxTextChars).Returns(20000);
        _config.SetupGet(c => c.WebScrapingMaxLinksPerPage).Returns(50);
        _config.SetupGet(c => c.WebScrapingMaxQueryVariantsPerPath).Returns(3);
        _config.SetupGet(c => c.WebScrapingDevAllowedHosts).Returns("");

        _source = NewSource(Root + "/", WebsiteCrawlMode.CrawlSite);
        _run = NewRun();
        _websites.Setup(w => w.GetRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _run);
        _websites.Setup(w => w.GetSourceAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _source);
        _websites.Setup(w => w.GetSourceByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _source);
        _websites.Setup(w => w.MapPagesByUrlAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _existing);
        _websites.Setup(w => w.AddPage(It.IsAny<KnowledgeWebsitePage>())).Callback<KnowledgeWebsitePage>(_addedPages.Add);
        _fetch.Setup(f => f.FetchRobotsAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _robots);
        _fetch.Setup(f => f.FetchPageAsync(It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<Uri, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri u, string? _, string? _, Func<Uri, bool> _, CancellationToken _) =>
                _site.TryGetValue(u.ToString(), out var r) ? r : new FetchResult { RequestedUrl = u.ToString(), FinalUrl = u.ToString(), StatusCode = 404, ErrorKind = FetchErrorKind.HttpError, Error = "HTTP 404" });
        _documents.Setup(d => d.ExistsAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _knowledge.Setup(k => k.CreateFromSourceAsync(_clinicId, It.IsAny<ExternalKnowledgeDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ExternalKnowledgeDocument d, CancellationToken _) =>
            {
                _created.Add((d.Title, d.Content, d.SourceUrl));
                return Doc(Guid.NewGuid(), d.IsActive);
            });
        _knowledge.Setup(k => k.ReplaceSourceContentAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, string _, string content, CancellationToken _) => { _replaced.Add((id, content)); return Doc(id, true); });
        _knowledge.Setup(k => k.DeleteAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Callback<Guid, Guid, CancellationToken>((_, id, _) => _deleted.Add(id)).ReturnsAsync(true);
        _knowledge.Setup(k => k.SetActiveAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, bool, CancellationToken>((_, id, a, _) => _toggled.Add((id, a)));
    }

    private KnowledgeDocumentResponse Doc(Guid id, bool active) =>
        new(id, _clinicId, "t", "general", "c", active, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "website", null, null, null, null);

    private KnowledgeWebsiteSource NewSource(string url, string mode) => new()
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, StartUrl = url, NormalizedStartUrl = url, Host = "8.8.8.8", CrawlMode = mode, Category = "general", IsActive = true, Status = WebsiteScrapeStatus.Pending
    };

    private KnowledgeWebsiteScrapeRun NewRun() => new() { Id = Guid.NewGuid(), ClinicId = _clinicId, WebsiteSourceId = _source.Id, Status = WebsiteScrapeStatus.Pending };

    private WebsiteScrapeProcessor Sut()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return new WebsiteScrapeProcessor(_websites.Object, _documents.Object, _uow.Object, _knowledge.Object, _fetch.Object, new HtmlContentExtractor(),
            new SsrfGuard(_config.Object, env.Object), _config.Object, NullLogger<WebsiteScrapeProcessor>.Instance);
    }

    private static string Text(string seed) => string.Join(" ", Enumerable.Repeat($"{seed} describes this procedure in a reasonable amount of detail.", 4));

    private static string Html(string title, string body, string head = "", params string[] links) =>
        $"<html><head><title>{title}</title>{head}</head><body><main><h1>{title}</h1><p>{body}</p>{string.Concat(links.Select(l => $"<a href=\"{l}\">link</a>"))}</main></body></html>";

    private void Page(string path, string html, int status = 200, string? finalPath = null, string? etag = null) =>
        _site[Root + path] = new FetchResult { RequestedUrl = Root + path, FinalUrl = Root + (finalPath ?? path), StatusCode = status, ContentType = "text/html", Html = html, ETag = etag };

    private void Fail(string path, int status) =>
        _site[Root + path] = new FetchResult { RequestedUrl = Root + path, FinalUrl = Root + path, StatusCode = status, ErrorKind = FetchErrorKind.HttpError, Error = $"HTTP {status}" };

    private Task Run() => Sut().RunAsync(_run.Id, CancellationToken.None);

    private KnowledgeWebsitePage PageRow(string path) => _existing[Root + path];

    // ---------------------------------------------------------------- run lifecycle

    [Fact]
    public async Task Runs_that_are_missing_or_not_pending_are_ignored()
    {
        _run.Status = WebsiteScrapeStatus.Completed;
        await Run();
        _fetch.Verify(f => f.FetchRobotsAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        _websites.Setup(w => w.GetRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeWebsiteScrapeRun?)null);
        await Run();
    }

    [Fact]
    public async Task A_run_whose_source_was_deleted_fails_cleanly()
    {
        _websites.Setup(w => w.GetSourceAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeWebsiteSource?)null);
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Failed, _run.Status);
        Assert.Contains("no longer exists", _run.ErrorSummary);
    }

    [Fact]
    public async Task An_unsafe_start_address_fails_the_run_without_fetching()
    {
        _source = NewSource("http://127.0.0.1/", WebsiteCrawlMode.CrawlSite);
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Failed, _run.Status);
        Assert.Equal(WebsiteScrapeStatus.Failed, _source.Status);
        _fetch.Verify(f => f.FetchPageAsync(It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<Uri, bool>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unreadable_robots_txt_stops_the_crawl()
    {
        _robots = new RobotsFetchResult(null, true, "timeout");
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Failed, _run.Status);
        Assert.Contains("robots.txt", _run.ErrorSummary);
        _knowledge.Verify(k => k.CreateFromSourceAsync(It.IsAny<Guid>(), It.IsAny<ExternalKnowledgeDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unexpected_error_marks_the_run_failed_and_rolls_back_pending_changes()
    {
        _fetch.Setup(f => f.FetchRobotsAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Failed, _run.Status);
        Assert.Contains("boom", _run.ErrorSummary);
        _uow.Verify(u => u.DiscardChanges(), Times.Once);
    }

    [Fact]
    public async Task Cancellation_during_shutdown_is_recorded_as_an_interruption()
    {
        using var cts = new CancellationTokenSource();
        _fetch.Setup(f => f.FetchRobotsAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Returns(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        await Sut().RunAsync(_run.Id, cts.Token);
        Assert.Contains("shutting down", _run.ErrorSummary);
    }

    // ---------------------------------------------------------------- crawling & indexing

    [Fact]
    public async Task A_single_page_source_indexes_only_the_start_page()
    {
        _source = NewSource(Root + "/", WebsiteCrawlMode.SinglePage);
        Page("/", Html("Home", Text("Home"), links: "/a"));
        Page("/a", Html("A", Text("A")));
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Completed, _run.Status);
        Assert.Equal((1, 1), (_run.PagesIndexed, _created.Count));
        Assert.Equal(Root + "/", _created[0].Url);
        Assert.NotNull(_source.LastScrapedAt);
    }

    [Fact]
    public async Task A_site_crawl_follows_internal_links_but_not_external_assets_or_documents()
    {
        Page("/", Html("Home", Text("Home"), links: ["/a", "/b/", "https://other.com/x", "/logo.png", "/brochure.pdf", "#top"]));
        Page("/a", Html("Service A", Text("Service A")));
        Page("/b", Html("Service B", Text("Service B")));
        await Run();

        Assert.Equal(WebsiteScrapeStatus.Completed, _run.Status);
        Assert.Equal(3, _created.Count);
        Assert.Equal(["Home", "Service A", "Service B"], _created.Select(c => c.Title).OrderBy(t => t));
        Assert.Equal(WebsitePageStatus.Skipped, PageRow("/brochure.pdf").Status);
        Assert.Contains("Upload Document", PageRow("/brochure.pdf").FailureReason);
        Assert.DoesNotContain(_existing.Keys, k => k.Contains("other.com") || k.EndsWith(".png"));
        Assert.Equal((3, 3), (_run.PagesIndexed, _run.PagesNew));
        Assert.Equal(1, _created.Count(c => c.Url == Root + "/"));
    }

    [Fact]
    public async Task Pages_blocked_by_robots_are_skipped_and_not_fetched()
    {
        _robots = new RobotsFetchResult(RobotsTxt.Parse("User-agent: *\nDisallow: /private", "sculptflowbot"), false, null);
        Page("/", Html("Home", Text("Home"), links: "/private/page"));
        Page("/private/page", Html("Secret", Text("Secret")));
        await Run();
        Assert.Equal(WebsitePageStatus.Skipped, PageRow("/private/page").Status);
        Assert.Contains("robots.txt", PageRow("/private/page").FailureReason);
        Assert.Single(_created);
    }

    [Fact]
    public async Task Noindex_listing_archive_and_thin_pages_are_skipped_with_reasons()
    {
        Page("/", Html("Home", Text("Home"), links: ["/secret", "/blog/category/news", "/archive", "/thin"]));
        Page("/secret", Html("Secret", Text("Secret"), head: "<meta name=\"robots\" content=\"noindex\">"));
        Page("/blog/category/news", Html("News", Text("News")));
        Page("/archive", Html("Monthly Archives", Text("Archive")));
        Page("/thin", "<html><head><title>Thin</title></head><body><main><p>Hi</p></main></body></html>");
        await Run();
        Assert.Contains("noindex", PageRow("/secret").FailureReason);
        Assert.Contains("listing/archive", PageRow("/blog/category/news").FailureReason);
        Assert.Contains("listing/archive", PageRow("/archive").FailureReason);
        Assert.Contains("Too little text", PageRow("/thin").FailureReason);
        Assert.Equal(4, _run.PagesSkipped);
    }

    [Fact]
    public async Task Duplicate_content_is_indexed_once()
    {
        var same = Html("Same", Text("Identical"));
        Page("/", Html("Home", Text("Home"), links: ["/x", "/y"]));
        Page("/x", same);
        Page("/y", same);
        await Run();
        Assert.Equal(2, _created.Count); // home + one copy
        Assert.Equal(1, _run.PagesDuplicate);
        var statuses = new[] { PageRow("/x").Status, PageRow("/y").Status };
        Assert.Contains(WebsitePageStatus.Duplicate, statuses);
        Assert.Contains(WebsitePageStatus.Indexed, statuses);
    }

    [Fact]
    public async Task A_canonical_pointing_at_another_page_makes_this_one_an_alias()
    {
        Page("/", Html("Home", Text("Home"), links: ["/amp", "/real"]));
        Page("/amp", Html("Real", Text("Real"), head: $"<link rel=\"canonical\" href=\"{Root}/real\">"));
        Page("/real", Html("Real", Text("Real")));
        await Run();
        Assert.Equal(WebsitePageStatus.Duplicate, PageRow("/amp").Status);
        Assert.Equal(Root + "/real", PageRow("/amp").CanonicalUrl);
        Assert.Equal(WebsitePageStatus.Indexed, PageRow("/real").Status);
        Assert.Equal(PageRow("/real").Id, PageRow("/amp").DuplicateOfPageId);
    }

    [Fact]
    public async Task A_redirect_is_followed_and_recorded_as_an_alias_of_the_final_page()
    {
        Page("/", Html("Home", Text("Home"), links: "/old"));
        Page("/old", Html("New home", Text("New")), finalPath: "/new");
        await Run();
        Assert.Equal(WebsitePageStatus.Duplicate, PageRow("/old").Status);
        Assert.Contains("Redirects to", PageRow("/old").FailureReason);
        Assert.Equal(WebsitePageStatus.Indexed, PageRow("/new").Status);
    }

    [Fact]
    public async Task Crawl_traps_are_not_followed()
    {
        Page("/", Html("Home", Text("Home"), links: ["/cart/items", "/shop?sort=price", "/a/a/a/page", "/ok"]));
        Page("/ok", Html("OK", Text("OK")));
        await Run();
        Assert.Equal(2, _created.Count);
        Assert.DoesNotContain(_existing.Keys, k => k.Contains("cart") || k.Contains("sort="));
    }

    [Fact]
    public async Task The_page_limit_truncates_the_crawl_and_prevents_removals()
    {
        _config.SetupGet(c => c.WebScrapingMaxPages).Returns(2);
        var gone = new KnowledgeWebsitePage { Id = Guid.NewGuid(), NormalizedUrl = Root + "/old", Url = Root + "/old", KnowledgeDocumentId = Guid.NewGuid(), Status = WebsitePageStatus.Indexed };
        _existing[gone.NormalizedUrl] = gone;
        Page("/", Html("Home", Text("Home"), links: ["/a", "/b", "/c"]));
        Page("/a", Html("A", Text("A")));
        Page("/b", Html("B", Text("B")));
        await Run();
        Assert.Contains("Crawl limits reached", _run.ErrorSummary);
        Assert.Equal(WebsitePageStatus.Indexed, gone.Status);
        Assert.Equal(0, gone.MissingCount);
    }

    // ---------------------------------------------------------------- re-scrape behaviour

    [Fact]
    public async Task A_second_crawl_with_the_same_content_changes_nothing_and_changed_content_replaces_the_document()
    {
        Page("/", Html("Home", Text("Home")));
        await Run();
        var docId = PageRow("/").KnowledgeDocumentId;
        Assert.NotNull(docId);

        _run = NewRun();
        await Run();
        Assert.Equal((1, 0, 1), (_run.PagesUnchanged, _run.PagesIndexed, _created.Count));
        Assert.Empty(_replaced);

        Page("/", Html("Home", Text("Home") + " Now with a brand new sentence about recovery times."));
        _run = NewRun();
        await Run();
        Assert.Equal((1, 1), (_run.PagesChanged, _replaced.Count));
        Assert.Equal(docId, _replaced[0].Id);
        Assert.Single(_created);
    }

    [Fact]
    public async Task A_304_not_modified_keeps_the_page_unchanged_and_conditional_headers_are_sent()
    {
        Page("/", Html("Home", Text("Home")), etag: "\"v1\"");
        await Run();
        _site[Root + "/"] = new FetchResult { RequestedUrl = Root + "/", FinalUrl = Root + "/", StatusCode = 304, NotModified = true };
        _run = NewRun();
        await Run();
        _fetch.Verify(f => f.FetchPageAsync(It.IsAny<Uri>(), "\"v1\"", It.IsAny<string?>(), It.IsAny<Func<Uri, bool>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, _run.PagesUnchanged);
        Assert.Equal(WebsitePageStatus.Unchanged, PageRow("/").Status);
    }

    [Fact]
    public async Task A_page_that_disappears_is_removed_only_after_two_missed_crawls()
    {
        Page("/", Html("Home", Text("Home"), links: "/a"));
        Page("/a", Html("A", Text("A")));
        await Run();
        var docA = PageRow("/a").KnowledgeDocumentId!.Value;

        Page("/", Html("Home", Text("Home")));  // no longer links to /a
        _run = NewRun();
        await Run();
        Assert.Equal(1, PageRow("/a").MissingCount);
        Assert.Equal(WebsitePageStatus.Indexed, PageRow("/a").Status);

        _run = NewRun();
        await Run();
        Assert.Equal(WebsitePageStatus.Removed, PageRow("/a").Status);
        Assert.Contains((docA, false), _toggled);
        Assert.Equal(1, _run.PagesRemoved);
    }

    [Fact]
    public async Task A_404_marks_the_page_failed_then_removed_and_a_410_removes_it_at_once()
    {
        Page("/", Html("Home", Text("Home"), links: ["/a", "/b"]));
        Page("/a", Html("A", Text("A")));
        Page("/b", Html("B", Text("B")));
        await Run();

        Fail("/a", 404);
        Fail("/b", 410);
        _run = NewRun();
        await Run();
        Assert.Equal(WebsitePageStatus.Failed, PageRow("/a").Status);
        Assert.Contains("will be removed", PageRow("/a").FailureReason);
        Assert.Equal(WebsitePageStatus.Removed, PageRow("/b").Status);

        _run = NewRun();
        await Run();
        Assert.Equal(WebsitePageStatus.Removed, PageRow("/a").Status);
    }

    [Fact]
    public async Task A_failing_start_page_fails_the_run_and_removes_nothing()
    {
        Page("/", Html("Home", Text("Home"), links: "/a"));
        Page("/a", Html("A", Text("A")));
        await Run();
        Fail("/", 500);
        _run = NewRun();
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Failed, _run.Status);
        Assert.Equal(WebsitePageStatus.Indexed, PageRow("/a").Status);
    }

    [Fact]
    public async Task Other_page_failures_complete_the_run_with_errors()
    {
        Page("/", Html("Home", Text("Home"), links: "/broken"));
        Fail("/broken", 500);
        await Run();
        Assert.Equal(WebsiteScrapeStatus.CompletedWithErrors, _run.Status);
        Assert.Contains("/broken", _run.ErrorSummary);
        Assert.Equal(1, _run.PagesFailed);
    }

    [Fact]
    public async Task Non_html_and_external_redirect_responses_are_skipped_not_failed()
    {
        Page("/", Html("Home", Text("Home"), links: ["/data", "/away"]));
        _site[Root + "/data"] = new FetchResult { RequestedUrl = Root + "/data", FinalUrl = Root + "/data", StatusCode = 200, ErrorKind = FetchErrorKind.NotHtml, Error = "Not an HTML page" };
        _site[Root + "/away"] = new FetchResult { RequestedUrl = Root + "/away", FinalUrl = "https://elsewhere.com/", StatusCode = 302, ErrorKind = FetchErrorKind.ExternalRedirect, Error = "Redirects off-site" };
        await Run();
        Assert.Equal(WebsiteScrapeStatus.Completed, _run.Status);
        Assert.Equal(2, _run.PagesSkipped);
    }

    [Fact]
    public async Task A_page_that_cannot_be_indexed_is_recorded_as_failed_without_stopping_the_crawl()
    {
        Page("/", Html("Home", Text("Home"), links: "/a"));
        Page("/a", Html("A", Text("A")));
        _knowledge.Setup(k => k.CreateFromSourceAsync(_clinicId, It.Is<ExternalKnowledgeDocument>(d => d.Title == "A"), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("bad content"));
        await Run();
        Assert.Equal(WebsitePageStatus.Failed, PageRow("/a").Status);
        Assert.Contains("bad content", PageRow("/a").FailureReason);
        Assert.Equal(WebsiteScrapeStatus.CompletedWithErrors, _run.Status);
        Assert.Equal(WebsitePageStatus.Indexed, PageRow("/").Status);
    }

    [Fact]
    public async Task Repeated_footer_blocks_are_stripped_as_boilerplate_on_non_start_pages()
    {
        string P(string name) => Html(name, Text(name) + " <p>Shared marketing banner repeated on every single page of this site.</p>");
        Page("/", Html("Home", Text("Home") + " <p>Shared marketing banner repeated on every single page of this site.</p>", links: ["/a", "/b", "/c"]));
        Page("/a", P("A"));
        Page("/b", P("B"));
        Page("/c", P("C"));
        await Run();
        Assert.Contains("Shared marketing banner", _created.Single(c => c.Title == "Home").Content);
        Assert.All(_created.Where(c => c.Title != "Home"), c => Assert.DoesNotContain("Shared marketing banner", c.Content));
        Assert.False(string.IsNullOrEmpty(_source.BoilerplateBlockHashes));
    }
}
