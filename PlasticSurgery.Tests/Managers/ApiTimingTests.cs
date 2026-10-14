using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Contracts.Services.Monitoring;
using PlasticSurgery.Business.Jobs;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Services.Monitoring;
using PlasticSurgery.Business.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Admin;
using PlasticSurgery.Middleware;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Tests.Managers;

/// <summary>The in-memory per-route timing window: counts, status classes, p95, bounded memory and exact drains.</summary>
public class ApiTimingCollectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 12, 0, 0, 20, TimeSpan.Zero);

    private static ApiTimingCollector Sut() => new(new ManualTimeProvider(Start));

    [Fact]
    public void Drain_summarises_each_method_and_route_and_starts_a_new_window()
    {
        var sut = Sut();
        for (var ms = 1; ms <= 100; ms++) sut.Record("GET", "/api/leads", ms <= 98 ? 200 : 500, ms, ms >= 90);
        sut.Record("GET", "/api/leads", 404, 5, false);
        sut.Record("POST", "/api/leads", 201, 40, false);

        var rows = sut.Drain(Start.AddMinutes(1));

        Assert.Equal(2, rows.Count);
        var get = rows.Single(r => r.Method == "GET");
        Assert.Equal("/api/leads", get.Route);
        Assert.Equal(101, get.RequestCount);
        Assert.Equal(2, get.ErrorCount);
        Assert.Equal(1, get.ClientErrorCount);
        Assert.Equal(11, get.SlowCount);
        Assert.Equal(5055, get.TotalMs);
        Assert.Equal(100, get.MaxMs);
        Assert.Equal(95, get.P95Ms); // nearest rank: the 96th of 101 sorted values (5,1,2,...,100 → 95)
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero), get.BucketStart);
        var post = rows.Single(r => r.Method == "POST");
        Assert.Equal((1, 40d, 40d), (post.RequestCount, post.P95Ms, post.MaxMs));

        Assert.Empty(sut.Drain(Start.AddMinutes(2)));
        sut.Record("GET", "/x", 200, 1, false);
        // The window began at the previous drain (12:02:00.020), so its rows are named 12:02.
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 12, 2, 0, TimeSpan.Zero), Assert.Single(sut.Drain(Start.AddMinutes(3))).BucketStart);
    }

    [Fact]
    public void Bucket_is_the_nearest_whole_minute_to_the_window_start()
    {
        var sut = new ApiTimingCollector(new ManualTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 45, TimeSpan.Zero)));
        sut.Record("GET", "/x", 200, 1, false);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 12, 1, 0, TimeSpan.Zero), Assert.Single(sut.Drain(Start)).BucketStart);
    }

    [Fact]
    public void Samples_are_bounded_and_the_p95_still_reflects_the_distribution()
    {
        var sut = Sut();
        const int n = ApiTimingLimits.MaxSamplesPerRoute * 5;
        for (var i = 1; i <= n; i++) sut.Record("GET", "/busy", 200, i, false);

        var row = Assert.Single(sut.Drain(Start.AddMinutes(1)));
        Assert.Equal(n, row.RequestCount);
        Assert.Equal(n, row.MaxMs);
        // A uniform sample of 1..n: the p95 lands near 0.95n (well within 5% for 2000 samples).
        Assert.InRange(row.P95Ms, n * 0.90, n * 1.0);
    }

    [Fact]
    public void Routes_beyond_the_window_limit_are_counted_under_other()
    {
        var sut = Sut();
        for (var i = 0; i < ApiTimingLimits.MaxRoutesPerWindow + 5; i++) sut.Record("GET", "/r" + i, 200, 1, false);
        sut.Record("GET", "/r0", 200, 1, false); // a route already in the window keeps its own row

        var rows = sut.Drain(Start.AddMinutes(1));
        Assert.Equal(ApiTimingLimits.MaxRoutesPerWindow + 1, rows.Count);
        Assert.Equal(5, rows.Single(r => r.Route == ApiTimingLimits.OtherRoute).RequestCount);
        Assert.Equal(2, rows.Single(r => r.Route == "/r0").RequestCount);
    }

    [Fact]
    public async Task Concurrent_records_are_never_lost_across_drains()
    {
        var sut = Sut();
        var drained = new List<ApiRouteStat>();
        var writers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++) sut.Record("GET", "/c", 200, 1, false);
        })).ToArray();
        while (!writers.All(w => w.IsCompleted)) drained.AddRange(sut.Drain(Start));
        await Task.WhenAll(writers);
        drained.AddRange(sut.Drain(Start));

        Assert.Equal(40000, drained.Sum(r => r.RequestCount));
    }
}

/// <summary>The middleware times by route template, logs slow requests, and stays out of the way when off.</summary>
public class RequestTimingMiddlewareTests
{
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IApiTimingCollector> _collector = new();
    private readonly ListLogger _logger = new();

    public RequestTimingMiddlewareTests()
    {
        _config.SetupGet(c => c.MonitoringEnabled).Returns(true);
        _config.SetupGet(c => c.MonitoringSlowRequestMs).Returns(1000);
    }

    private RequestTimingMiddleware Sut(RequestDelegate next) => new(next, _config.Object, _collector.Object, _logger);

    private static DefaultHttpContext Request(string path, string? template, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (template is not null)
        {
            context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, "test"));
        }
        return context;
    }

    [Fact]
    public async Task Records_the_route_template_not_the_path()
    {
        var context = Request("/api/leads/7f1c", "api/leads/{id}");
        await Sut(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; }).InvokeAsync(context);

        _collector.Verify(c => c.Record("GET", "/api/leads/{id}", 404, It.Is<double>(ms => ms >= 0), false), Times.Once);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Unmatched_requests_share_one_route_name()
    {
        await Sut(_ => Task.CompletedTask).InvokeAsync(Request("/wp-admin/setup", null));
        _collector.Verify(c => c.Record("GET", ApiTimingLimits.UnmatchedRoute, 200, It.IsAny<double>(), false), Times.Once);
    }

    [Fact]
    public async Task A_request_over_the_threshold_is_logged_with_the_route_only()
    {
        _config.SetupGet(c => c.MonitoringSlowRequestMs).Returns(50);
        var context = Request("/api/leads/secret-token", "api/leads/{id}", "POST");
        await Sut(async _ => await Task.Delay(80)).InvokeAsync(context);

        _collector.Verify(c => c.Record("POST", "/api/leads/{id}", 200, It.Is<double>(ms => ms >= 50), true), Times.Once);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("/api/leads/{id}", entry.Message);
        Assert.DoesNotContain("secret-token", entry.Message);
    }

    [Fact]
    public async Task An_unhandled_exception_counts_as_a_500_and_still_propagates()
    {
        var context = Request("/api/x", "api/x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut(_ => throw new InvalidOperationException("boom")).InvokeAsync(context));
        _collector.Verify(c => c.Record("GET", "/api/x", 500, It.IsAny<double>(), false), Times.Once);
    }

    [Fact]
    public async Task Off_static_files_hubs_and_status_reexecutes_are_not_recorded()
    {
        var calls = 0;
        RequestDelegate next = _ => { calls++; return Task.CompletedTask; };

        await Sut(next).InvokeAsync(Request("/css/site.css", "css/site.css"));
        await Sut(next).InvokeAsync(Request("/hubs/inbox", "hubs/inbox"));
        var reexecute = Request("/Status", "Status");
        reexecute.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature());
        await Sut(next).InvokeAsync(reexecute);
        _config.SetupGet(c => c.MonitoringEnabled).Returns(false);
        await Sut(next).InvokeAsync(Request("/api/x", "api/x"));

        Assert.Equal(4, calls);
        _collector.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Route_names_are_rooted_and_capped()
    {
        Assert.Equal("/", RequestTimingMiddleware.RouteOf(Request("/", "")));
        Assert.Equal("/Leads/Details", RequestTimingMiddleware.RouteOf(Request("/Leads/Details", "/Leads/Details")));
        var longTemplate = "api/" + new string('a', 400);
        Assert.Equal(ApiTimingLimits.MaxRouteLength, RequestTimingMiddleware.RouteOf(Request("/x", longTemplate)).Length);
    }

    private sealed class ListLogger : ILogger<RequestTimingMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>Saving, trimming and flushing the per-minute rows.</summary>
public class ApiTimingServiceAndJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_stamps_and_writes_rows_and_skips_an_empty_minute()
    {
        var repo = new Mock<IApiRouteStatRepository>();
        var unit = new Mock<IUnitOfWork>();
        var sut = new ApiTimingService(repo.Object, unit.Object, Mock.Of<IConfigManager>(), new ManualTimeProvider(Now));

        await sut.SaveAsync([], default);
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var row = new ApiRouteStat { Method = "GET", Route = "/x", RequestCount = 1 };
        await sut.SaveAsync([row], default);
        Assert.Equal(Now, row.CreatedAt);
        repo.Verify(r => r.AddRange(It.Is<IEnumerable<ApiRouteStat>>(rows => rows.Single() == row)), Times.Once);
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Purge_deletes_rows_older_than_the_retention_setting()
    {
        var repo = new Mock<IApiRouteStatRepository>();
        repo.Setup(r => r.DeleteOlderThanAsync(Now.AddDays(-7), It.IsAny<CancellationToken>())).ReturnsAsync(3).Verifiable();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.MonitoringRetentionDays).Returns(7);
        var sut = new ApiTimingService(repo.Object, Mock.Of<IUnitOfWork>(), config.Object, new ManualTimeProvider(Now));

        Assert.Equal(3, await sut.PurgeAsync(default));
        repo.Verify();
    }

    private static (ApiTimingFlushJob Job, Mock<IApiTimingService> Service, ApiTimingCollector Collector, ManualTimeProvider Clock) Job()
    {
        var clock = new ManualTimeProvider(Now);
        var collector = new ApiTimingCollector(clock);
        var service = new Mock<IApiTimingService>();
        var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var job = new ApiTimingFlushJob(collector, services.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<ApiTimingFlushJob>.Instance);
        return (job, service, collector, clock);
    }

    [Fact]
    public async Task Flush_writes_the_minute_and_purges_only_once_per_interval()
    {
        var (job, service, collector, clock) = Job();
        collector.Record("GET", "/x", 200, 10, false);

        await job.FlushAsync(default);
        service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<ApiRouteStat>>(rows => rows.Single().Route == "/x"), It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Once);

        clock.Advance(TimeSpan.FromMinutes(1));
        await job.FlushAsync(default); // nothing recorded and no purge due: no database work at all
        service.Verify(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<ApiRouteStat>>(), It.IsAny<CancellationToken>()), Times.Once);

        clock.Advance(TimeSpan.FromMinutes(ApiTimingLimits.PurgeIntervalMinutes));
        await job.FlushAsync(default);
        service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_failed_write_drops_the_minute_instead_of_throwing()
    {
        var (job, service, collector, _) = Job();
        service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<ApiRouteStat>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("db down"));
        collector.Record("GET", "/x", 200, 10, false);

        await job.FlushAsync(default);
        Assert.Empty(collector.Drain(Now)); // not kept for a retry

        service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<ApiRouteStat>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        collector.Record("GET", "/x", 200, 10, false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => job.FlushAsync(default));
    }

    [Fact]
    public async Task Stopping_flushes_the_last_partial_minute()
    {
        var (job, service, collector, _) = Job();
        collector.Record("GET", "/x", 200, 10, false);
        await job.StopAsync(default);
        service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<ApiRouteStat>>(rows => rows.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }
}

/// <summary>The platform-admin report: sort keys, clamping, totals across every route.</summary>
public class ApiTimingAdminTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<IApiRouteStatRepository> _repo = new();
    private readonly Mock<IConfigManager> _config = new();

    private static RouteTimingRow Row(string route, long requests, double avg, long errors = 0, double p95 = 0, double max = 0, long slow = 0) =>
        new("GET", route, requests, errors, 0, slow, avg, p95, max, requests * avg / 1000, Now);

    private ApiTimingAdminService Sut()
    {
        _repo.Setup(r => r.SummariseSinceReadOnlyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Row("/a", 10, 500, errors: 1, p95: 900, max: 2000, slow: 2),
            Row("/b", 1000, 20, errors: 5, p95: 40, max: 300),
            Row("/c", 3, 50, p95: 60, max: 60)
        ]);
        _config.SetupGet(c => c.MonitoringEnabled).Returns(true);
        _config.SetupGet(c => c.MonitoringSlowRequestMs).Returns(1000);
        _config.SetupGet(c => c.MonitoringRetentionDays).Returns(30);
        return new ApiTimingAdminService(_repo.Object, _config.Object, new ManualTimeProvider(Now));
    }

    [Theory]
    [InlineData(null, "/a")]
    [InlineData("slowest", "/a")]
    [InlineData("CALLS", "/b")]
    [InlineData("errors", "/b")]
    [InlineData("p95", "/a")]
    [InlineData("max", "/a")]
    [InlineData("slow", "/a")]
    [InlineData("time", "/b")]
    public async Task Sorts_descending_by_the_chosen_measure(string? sort, string first)
    {
        var report = await Sut().ReportAsync(24, sort, 50, default);
        Assert.Equal(first, report.Routes[0].Route);
        Assert.Equal(3, report.Routes.Count);
    }

    [Fact]
    public async Task Totals_cover_every_route_even_when_only_some_are_returned()
    {
        var report = await Sut().ReportAsync(24, "slowest", 1, default);
        Assert.Equal("/a", Assert.Single(report.Routes).Route);
        Assert.Equal((1013L, 6L, 2L, 3), (report.TotalRequests, report.TotalErrors, report.TotalSlowRequests, report.RouteCount));
        Assert.Equal((Now.AddHours(-24), Now, 24, "slowest"), (report.From, report.To, report.Hours, report.Sort));
        Assert.Equal((true, 1000, 30), (report.Enabled, report.SlowRequestMs, report.RetentionDays));
    }

    [Fact]
    public async Task Period_and_take_are_clamped_and_an_unknown_sort_is_rejected()
    {
        var sut = Sut();
        var report = await sut.ReportAsync(1_000_000, null, 1_000_000, default);
        Assert.Equal(ApiTimingLimits.MaxReportHours, report.Hours);
        _repo.Verify(r => r.SummariseSinceReadOnlyAsync(Now.AddHours(-ApiTimingLimits.MaxReportHours), It.IsAny<CancellationToken>()));
        Assert.Equal(1, (await sut.ReportAsync(-5, null, 10, default)).Hours);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.ReportAsync(24, "fastest", 10, default));
    }

    [Fact]
    public async Task Controller_applies_defaults_for_missing_query_values()
    {
        var service = new Mock<IApiTimingAdminService>();
        service.Setup(s => s.ReportAsync(ApiTimingLimits.DefaultReportHours, null, ApiTimingLimits.DefaultReportTake, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiTimingReport(Now, Now, 24, "slowest", true, 1000, 30, 0, 0, 0, 0, [])).Verifiable();
        Assert.IsType<OkObjectResult>(await new ApiTimingsAdminController(service.Object).Report(0, null, 0, default));
        service.Verify();
    }
}
