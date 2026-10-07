using System.Threading.Channels;

namespace PlasticSurgery.Business.Contracts.Jobs;

/// <summary>Hands a queued crawl (a knowledge_website_scrape_runs id) to the background worker.</summary>
public interface IWebsiteScrapeQueue
{
    void Enqueue(Guid runId);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
