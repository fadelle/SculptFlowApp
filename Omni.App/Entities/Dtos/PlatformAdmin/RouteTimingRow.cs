namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>
/// One method + route over a period (summed from activity.api_route_stats). AvgMs is exact; P95Ms is the
/// request-weighted average of the per-minute p95s (an approximation: exact percentiles can't be combined);
/// MaxMs is the slowest single request. TotalSeconds = time spent in this route (where the server's time goes).
/// </summary>
public record RouteTimingRow(
    string Method,
    string Route,
    long Requests,
    long Errors,
    long ClientErrors,
    long SlowRequests,
    double AvgMs,
    double P95Ms,
    double MaxMs,
    double TotalSeconds,
    DateTimeOffset LastSeenAt);
