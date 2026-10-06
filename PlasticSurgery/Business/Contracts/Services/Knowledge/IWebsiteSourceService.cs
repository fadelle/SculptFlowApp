using PlasticSurgery.Entities.Requests.Knowledge;
using PlasticSurgery.Entities.Responses.Knowledge;

namespace PlasticSurgery.Business.Contracts.Services.Knowledge;

/// <summary>
/// What the dashboard does with website sources: add one (and start its first crawl), list/inspect, re-scrape,
/// activate/deactivate, delete. It only manages rows and queues work — the crawling itself is
/// <see cref="IWebsiteScrapeProcessor"/>'s job, run in the background. EVERY method takes the clinicId the
/// caller resolved from CurrentClinicContext and scopes every query by it.
/// Throws ArgumentException (staff-readable message) for invalid input or a refused action.
/// </summary>
public interface IWebsiteSourceService
{
    Task<WebsiteSourceResponse> CreateAsync(Guid clinicId, CreateWebsiteSourceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WebsiteSourceResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);
    Task<WebsiteSourceDetailResponse?> GetAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<WebsitePageResponse>> ListPagesAsync(Guid clinicId, Guid sourceId, string? status, int skip, int take, CancellationToken ct = default);
    /// <summary>Starts another crawl of the same website. Null if the source isn't in this clinic.</summary>
    Task<WebsiteSourceResponse?> RescrapeAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);
    /// <summary>Deactivating hides every page document of the website from AI search; activating restores them.</summary>
    Task<WebsiteSourceResponse?> SetActiveAsync(Guid clinicId, Guid sourceId, bool isActive, CancellationToken ct = default);
    /// <summary>Deletes the source, its page/run records AND the knowledge documents (with their chunks) that
    /// the crawl created. Manual entries, uploads, other websites and other clinics are untouched.</summary>
    Task<bool> DeleteAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);
}
