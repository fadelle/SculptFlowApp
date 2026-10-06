using System.Threading.Channels;
using PlasticSurgery.Business.Contracts.Engines.WebScraping;
using PlasticSurgery.Business.Contracts.Jobs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Business.Jobs;

/// <summary>
/// Background processing for website crawls. The request that starts a crawl only writes a `pending` run
/// row and enqueues its id, then returns immediately; this hosted service crawls runs one at a time, each in
/// its own DI scope. State lives in the database (run + page rows), not in memory, so progress is visible from
/// any page load, and a restart is recoverable: on startup, runs left `crawling` by a previous process are
/// marked failed ("interrupted") and runs still `pending` are queued again.
/// One crawl at a time is deliberate: it bounds load on both the target sites and the embedding provider.
/// </summary>
public sealed class WebsiteScrapeWorker : BackgroundService
{
    private readonly IWebsiteScrapeQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebsiteScrapeWorker> _logger;

    public WebsiteScrapeWorker(IWebsiteScrapeQueue queue, IServiceScopeFactory scopes, ILogger<WebsiteScrapeWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid runId;
            try
            {
                runId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IWebsiteScrapeProcessor>().RunAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The processor records its own failures; this only guards the loop itself.
                _logger.LogError(ex, "Website crawl worker failed while running {RunId}.", runId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var websites = scope.ServiceProvider.GetRequiredService<IWebsiteSourceRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var interrupted = await websites.ListRunsInStatusAsync(WebsiteScrapeStatus.Crawling, ct);
            foreach (var run in interrupted)
            {
                run.Status = WebsiteScrapeStatus.Failed;
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.ErrorSummary = "The crawl was interrupted by an application restart. Start it again.";
                var source = await websites.GetSourceByIdAsync(run.WebsiteSourceId, ct);
                if (source is not null) source.Status = WebsiteScrapeStatus.Failed;
            }
            await unitOfWork.SaveChangesAsync(ct);

            foreach (var id in await websites.ListRunIdsInStatusAsync(WebsiteScrapeStatus.Pending, ct))
            {
                _queue.Enqueue(id);
            }

            if (interrupted.Count > 0) _logger.LogWarning("Marked {Count} interrupted website crawl(s) as failed.", interrupted.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not recover website crawl state at startup.");
        }
    }
}
