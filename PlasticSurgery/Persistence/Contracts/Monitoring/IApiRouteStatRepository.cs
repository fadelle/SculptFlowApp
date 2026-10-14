using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Monitoring;

/// <summary>activity.api_route_stats: per-minute API timings (see ApiRouteStat).</summary>
public interface IApiRouteStatRepository
{
    /// <summary>Queues the rows; they are written with the caller's next save.</summary>
    void AddRange(IEnumerable<ApiRouteStat> rows);

    /// <summary>Set-based delete of every row whose minute started before <paramref name="cutoff"/>. Returns rows deleted.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    /// <summary>One summed row per method + route for minutes starting at or after <paramref name="from"/> (unsorted).</summary>
    Task<List<RouteTimingRow>> SummariseSinceReadOnlyAsync(DateTimeOffset from, CancellationToken ct = default);
}
