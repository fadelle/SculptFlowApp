namespace Omni.App.Entities.Models;

/// <summary>
/// One minute of timings for one method + route template on one app instance (activity.api_route_stats). Written by
/// ApiTimingFlushJob from what RequestTimingMiddleware recorded in memory. TotalMs (not an average) is stored so any
/// period's average is the sum of TotalMs over the sum of RequestCount; P95Ms is that minute's 95th percentile.
/// </summary>
public class ApiRouteStat
{
    public long Id { get; set; }

    /// <summary>Start of the minute (UTC) these requests finished in.</summary>
    public DateTimeOffset BucketStart { get; set; }

    public string Method { get; set; } = string.Empty;

    /// <summary>The matched route template ("/api/leads/{id}"), never the raw path; "(unmatched)" when nothing matched.</summary>
    public string Route { get; set; } = string.Empty;

    public int RequestCount { get; set; }

    /// <summary>Responses with a 5xx status (or an unhandled exception).</summary>
    public int ErrorCount { get; set; }

    /// <summary>Responses with a 4xx status.</summary>
    public int ClientErrorCount { get; set; }

    /// <summary>Requests at or over Monitoring:SlowRequestMs.</summary>
    public int SlowCount { get; set; }

    public double TotalMs { get; set; }
    public double P95Ms { get; set; }
    public double MaxMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
