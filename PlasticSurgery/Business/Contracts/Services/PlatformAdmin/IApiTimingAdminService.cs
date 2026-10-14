using PlasticSurgery.Entities.Dtos.PlatformAdmin;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>Read-only API timings (activity.api_route_stats) for the admin portal's API timings page.</summary>
public interface IApiTimingAdminService
{
    /// <summary>
    /// Routes seen in the last <paramref name="hours"/> hours (1 to ApiTimingLimits.MaxReportHours), sorted by
    /// <paramref name="sort"/> ("slowest" = average, "p95", "max", "calls", "errors", "slow", "time" = total time;
    /// default "slowest"), first <paramref name="take"/> (1 to ApiTimingLimits.MaxReportTake). An unknown sort is an
    /// ArgumentException (400).
    /// </summary>
    Task<ApiTimingReport> ReportAsync(int hours, string? sort, int take, CancellationToken ct);
}
