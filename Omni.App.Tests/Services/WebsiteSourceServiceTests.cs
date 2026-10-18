using Microsoft.Extensions.Hosting;
using Omni.App.Business.Engines.WebScraping;
using Omni.App.Business.Services.Knowledge;

namespace Omni.App.Tests.Services;

public class WebsiteSourceServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IWebsiteSourceRepository> _websites = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IWebsiteScrapeQueue> _queue = new();
    private readonly Mock<IConfigManager> _config = new();

    public WebsiteSourceServiceTests()
    {
        _config.SetupGet(c => c.WebScrapingMaxSourcesPerClient).Returns(3);
        _config.SetupGet(c => c.WebScrapingDevAllowedHosts).Returns("");
        _websites.Setup(w => w.CountIndexedPagesAsync(_clientId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
    }

    private WebsiteSourceService Sut()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return new WebsiteSourceService(_websites.Object, _uow.Object, _knowledge.Object, _queue.Object, new SsrfGuard(_config.Object, env.Object), _config.Object);
    }

    private KnowledgeWebsiteSource Source(bool active = true, string status = WebsiteScrapeStatus.Completed)
    {
        var s = new KnowledgeWebsiteSource { Id = Guid.NewGuid(), ClientId = _clientId, StartUrl = "https://8.8.8.8/", Host = "8.8.8.8", CrawlMode = WebsiteCrawlMode.CrawlSite, Category = "general", IsActive = active, Status = status };
        _websites.Setup(w => w.GetSourceAsync(_clientId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);
        _websites.Setup(w => w.GetSourceReadOnlyAsync(_clientId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);
        return s;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://8.8.8.8/")]
    [InlineData("https://user:pw@8.8.8.8/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("https://8.8.8.8:8443/")]
    public async Task Create_rejects_empty_unsafe_and_unsupported_addresses(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest(url, null, null)));
        _websites.Verify(w => w.AddSource(It.IsAny<KnowledgeWebsiteSource>()), Times.Never);
    }

    [Fact]
    public async Task Create_adds_https_normalises_stores_a_pending_run_and_queues_it_after_saving()
    {
        KnowledgeWebsiteSource? source = null;
        KnowledgeWebsiteScrapeRun? run = null;
        _websites.Setup(w => w.AddSource(It.IsAny<KnowledgeWebsiteSource>())).Callback<KnowledgeWebsiteSource>(s => source = s);
        _websites.Setup(w => w.AddRun(It.IsAny<KnowledgeWebsiteScrapeRun>())).Callback<KnowledgeWebsiteScrapeRun>(r => run = r);
        var order = new List<string>();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("save")).ReturnsAsync(1);
        _queue.Setup(q => q.Enqueue(It.IsAny<Guid>())).Callback<Guid>(_ => order.Add("enqueue"));

        var r = await Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest(" 8.8.8.8/About/?utm_source=x ", " Single_Page ", " FAQ ", false));

        Assert.Equal(("https://8.8.8.8/About", "8.8.8.8", WebsiteCrawlMode.SinglePage, "faq", false, WebsiteScrapeStatus.Pending), (source!.NormalizedStartUrl, source.Host, source.CrawlMode, source.Category, source.IsActive, source.Status));
        Assert.Equal(["save", "enqueue"], order);
        _queue.Verify(q => q.Enqueue(run!.Id), Times.Once);
        Assert.True(r.InProgress);
    }

    [Fact]
    public async Task Create_defaults_mode_and_category_and_enforces_limits_and_duplicates()
    {
        KnowledgeWebsiteSource? source = null;
        _websites.Setup(w => w.AddSource(It.IsAny<KnowledgeWebsiteSource>())).Callback<KnowledgeWebsiteSource>(s => source = s);
        await Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest("https://8.8.8.8", null, null));
        Assert.Equal((WebsiteCrawlMode.CrawlSite, KnowledgeCategory.General), (source!.CrawlMode, source.Category));

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest("https://8.8.8.8", "teleport", null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest("https://8.8.8.8", null, new string('c', 51))));

        _websites.Setup(w => w.CountSourcesAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest("https://8.8.8.8", null, null)));
        Assert.Contains("up to 3 websites", TextKit.English(ex));

        _websites.Setup(w => w.CountSourcesAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _websites.Setup(w => w.SourceUrlExistsAsync(_clientId, "https://8.8.8.8/", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest("https://8.8.8.8", null, null)));
    }

    [Fact]
    public async Task A_lost_duplicate_race_is_reported_and_nothing_is_queued()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateRecordException(new Exception("dup")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, new CreateWebsiteSourceRequest("https://8.8.8.8", null, null)));
        _queue.Verify(q => q.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task List_pairs_each_source_with_its_latest_run_and_indexed_count()
    {
        var a = Source();
        var b = Source(status: WebsiteScrapeStatus.Crawling);
        _websites.Setup(w => w.ListSourcesReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeWebsiteSource> { a, b });
        var older = new KnowledgeWebsiteScrapeRun { Id = Guid.NewGuid(), WebsiteSourceId = a.Id, Status = "completed", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var newer = new KnowledgeWebsiteScrapeRun { Id = Guid.NewGuid(), WebsiteSourceId = a.Id, Status = "completed_with_errors", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        _websites.Setup(w => w.ListRunsReadOnlyAsync(_clientId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeWebsiteScrapeRun> { older, newer });
        _websites.Setup(w => w.CountIndexedPagesAsync(_clientId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int> { [a.Id] = 12 });

        var list = await Sut().ListAsync(_clientId);

        Assert.Equal((12, newer.Id), (list[0].IndexedPages, list[0].LatestRun!.Id));
        Assert.Null(list[1].LatestRun);
        Assert.True(list[1].InProgress);
    }

    [Fact]
    public async Task Get_returns_detail_with_recent_runs_and_page_status_counts()
    {
        Assert.Null(await Sut().GetAsync(_clientId, Guid.NewGuid()));
        var s = Source();
        _websites.Setup(w => w.ListRecentRunsReadOnlyAsync(_clientId, s.Id, 8, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeWebsiteScrapeRun> { new() { Id = Guid.NewGuid(), Status = "completed" } });
        _websites.Setup(w => w.CountPagesByStatusAsync(_clientId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, int> { ["indexed"] = 4 });
        var d = await Sut().GetAsync(_clientId, s.Id);
        Assert.Equal((1, 4), (d!.RecentRuns.Count, d.PageStatusCounts["indexed"]));
    }

    [Fact]
    public async Task ListPages_clamps_paging()
    {
        _websites.Setup(w => w.ListPagesReadOnlyAsync(_clientId, It.IsAny<Guid>(), "indexed", 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeWebsitePage> { new() { Id = Guid.NewGuid(), NormalizedUrl = "https://x/", Status = "indexed" } });
        Assert.Single(await Sut().ListPagesAsync(_clientId, Guid.NewGuid(), "indexed", -5, 10_000));
    }

    [Fact]
    public async Task Rescrape_rules()
    {
        Assert.Null(await Sut().RescrapeAsync(_clientId, Guid.NewGuid()));
        var running = Source(status: WebsiteScrapeStatus.Crawling);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescrapeAsync(_clientId, running.Id));
        var hidden = Source(status: WebsiteScrapeStatus.Completed);
        _websites.Setup(w => w.HasRunInProgressAsync(hidden.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescrapeAsync(_clientId, hidden.Id));
        var inactive = Source(active: false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescrapeAsync(_clientId, inactive.Id));

        var ok = Source();
        KnowledgeWebsiteScrapeRun? run = null;
        _websites.Setup(w => w.AddRun(It.IsAny<KnowledgeWebsiteScrapeRun>())).Callback<KnowledgeWebsiteScrapeRun>(r => run = r);
        var r = await Sut().RescrapeAsync(_clientId, ok.Id);
        Assert.Equal(WebsiteScrapeStatus.Pending, ok.Status);
        _queue.Verify(q => q.Enqueue(run!.Id), Times.Once);
        Assert.NotNull(r);
    }

    [Fact]
    public async Task SetActive_toggles_the_source_and_its_documents()
    {
        Assert.Null(await Sut().SetActiveAsync(_clientId, Guid.NewGuid(), true));
        var s = Source();
        var docs = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _websites.Setup(w => w.ListPageDocumentIdsAsync(_clientId, s.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(docs);
        await Sut().SetActiveAsync(_clientId, s.Id, false);
        Assert.False(s.IsActive);
        foreach (var d in docs) _knowledge.Verify(k => k.SetActiveAsync(_clientId, d, false, It.IsAny<CancellationToken>()), Times.Once);

        _websites.Setup(w => w.ListPageDocumentIdsAsync(_clientId, s.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { docs[0] });
        await Sut().SetActiveAsync(_clientId, s.Id, true);
        _knowledge.Verify(k => k.SetActiveAsync(_clientId, docs[0], true, It.IsAny<CancellationToken>()), Times.Once);
        _knowledge.Verify(k => k.SetActiveAsync(_clientId, docs[1], true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_removes_documents_then_the_source_but_not_while_crawling()
    {
        Assert.False(await Sut().DeleteAsync(_clientId, Guid.NewGuid()));
        var busy = Source();
        _websites.Setup(w => w.HasRunInProgressAsync(busy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().DeleteAsync(_clientId, busy.Id));

        var s = Source();
        var doc = Guid.NewGuid();
        _websites.Setup(w => w.ListPageDocumentIdsAsync(_clientId, s.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { doc });
        Assert.True(await Sut().DeleteAsync(_clientId, s.Id));
        _knowledge.Verify(k => k.DeleteAsync(_clientId, doc, It.IsAny<CancellationToken>()), Times.Once);
        _websites.Verify(w => w.RemoveSource(s), Times.Once);
    }
}
