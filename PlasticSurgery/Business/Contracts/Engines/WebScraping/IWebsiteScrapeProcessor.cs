using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlasticSurgery.Business.Contracts.Engines.WebScraping;

/// <summary>Runs ONE crawl (a knowledge_website_scrape_runs row). Called by the background worker, never by a request.</summary>
public interface IWebsiteScrapeProcessor
{
    Task RunAsync(Guid runId, CancellationToken ct);
}
