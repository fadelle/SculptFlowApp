namespace Omni.App.Common.Statics;

/// <summary>Fixed shape of the API timing recorder (RequestTimingMiddleware, ApiTimingCollector, ApiTimingFlushJob).</summary>
public static class ApiTimingLimits
{
    /// <summary>One row per route per this many seconds; the flush job runs on whole minutes.</summary>
    public const int FlushSeconds = 60;

    /// <summary>How often the flush job deletes rows older than Monitoring:RetentionDays.</summary>
    public const int PurgeIntervalMinutes = 60;

    /// <summary>Durations kept per route per minute for the p95 (a uniform random sample beyond this).</summary>
    public const int MaxSamplesPerRoute = 2000;

    /// <summary>Distinct routes kept per minute; any further ones are counted under <see cref="OtherRoute"/>.</summary>
    public const int MaxRoutesPerWindow = 1000;

    /// <summary>Longest route template stored (activity.api_route_stats.route is varchar(300)).</summary>
    public const int MaxRouteLength = 300;

    /// <summary>Route name for a request no endpoint matched (404s, probes), so raw paths never become rows.</summary>
    public const string UnmatchedRoute = "(unmatched)";

    /// <summary>Route name for requests beyond <see cref="MaxRoutesPerWindow"/> in one minute.</summary>
    public const string OtherRoute = "(other)";

    /// <summary>Longest period (hours) the platform-admin report covers: the longest Monitoring:RetentionDays.</summary>
    public const int MaxReportHours = 24 * 365;

    public const int DefaultReportHours = 24;
    public const int DefaultReportTake = 50;
    public const int MaxReportTake = 500;
}
