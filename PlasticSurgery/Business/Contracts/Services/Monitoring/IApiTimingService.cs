using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Services.Monitoring;

/// <summary>Writes and trims activity.api_route_stats for ApiTimingFlushJob.</summary>
public interface IApiTimingService
{
    /// <summary>Saves one minute's summary rows (nothing when empty).</summary>
    Task SaveAsync(IReadOnlyCollection<ApiRouteStat> rows, CancellationToken ct);

    /// <summary>Deletes rows older than Monitoring:RetentionDays. Returns rows deleted.</summary>
    Task<int> PurgeAsync(CancellationToken ct);
}
