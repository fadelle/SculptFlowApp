using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Singleton, in memory. RequestTimingMiddleware records every finished request here (no database write per request);
/// ApiTimingFlushJob drains it once a minute into one activity.api_route_stats row per method + route.
/// </summary>
public interface IApiTimingCollector
{
    /// <summary>Adds one finished request to the current minute.</summary>
    void Record(string method, string route, int statusCode, double elapsedMs, bool slow);

    /// <summary>
    /// Ends the current window and returns its summary rows (BucketStart = when the window began), starting a new
    /// window at <paramref name="now"/>. Empty when nothing was recorded.
    /// </summary>
    List<ApiRouteStat> Drain(DateTimeOffset now);
}
