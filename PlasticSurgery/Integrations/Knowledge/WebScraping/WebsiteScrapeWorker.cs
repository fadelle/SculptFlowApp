using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>Hands a queued crawl (a knowledge_website_scrape_runs id) to the background worker.</summary>
public interface IWebsiteScrapeQueue
{
    void Enqueue(Guid runId);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}

public sealed class WebsiteScrapeQueue : IWebsiteScrapeQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid runId) => _channel.Writer.TryWrite(runId);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}

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
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var interrupted = await db.KnowledgeWebsiteScrapeRuns.Where(r => r.Status == WebsiteScrapeStatus.Crawling).ToListAsync(ct);
            foreach (var run in interrupted)
            {
                run.Status = WebsiteScrapeStatus.Failed;
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.ErrorSummary = "The crawl was interrupted by an application restart. Start it again.";
                var source = await db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.Id == run.WebsiteSourceId, ct);
                if (source is not null) source.Status = WebsiteScrapeStatus.Failed;
            }
            await db.SaveChangesAsync(ct);

            foreach (var id in await db.KnowledgeWebsiteScrapeRuns.Where(r => r.Status == WebsiteScrapeStatus.Pending)
                         .OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct))
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
