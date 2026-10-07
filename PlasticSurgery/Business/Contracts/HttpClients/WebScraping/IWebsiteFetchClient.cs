using System.Net;
using System.Text;
using PlasticSurgery.Entities.Dtos.WebScraping;

namespace PlasticSurgery.Business.Contracts.HttpClients.WebScraping;

/// <summary>
/// All HTTP the crawler does. Every request: SSRF-validated first, sent with the SculptFlow crawler
/// User-Agent, with a hard timeout; redirects are followed MANUALLY (never automatically) so each hop is
/// re-validated and can be refused if it leaves the configured site; responses must be HTML and are read up
/// to a size cap (after decompression). No cookies, no proxy, no credentials.
/// </summary>
public interface IWebsiteFetchClient
{
    Task<FetchResult> FetchPageAsync(Uri url, string? etag, string? lastModified, Func<Uri, bool> isSameSite, CancellationToken ct = default);
    Task<RobotsFetchResult> FetchRobotsAsync(Uri origin, CancellationToken ct = default);
}
