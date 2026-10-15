using PlasticSurgery.Entities.Dtos.PlatformAdmin;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// Read-only error log (activity.app_logs) for the admin portal's Errors page and Overview tile. Hours are clamped to
/// 1..AppLogLimits.MaxReportHours. <c>level</c> is a minimum: "warning" (default, everything), "error" (errors and
/// criticals) or "critical"; anything else, or an unknown sort, is an ArgumentException (400).
/// </summary>
public interface IAppLogAdminService
{
    /// <summary>One row per problem (fingerprint); <paramref name="sort"/> "recent" (default) or "count".</summary>
    Task<AppLogGroupReport> GroupsAsync(int hours, string? level, Guid? clinicId, string? search, string? sort, int take, CancellationToken ct);

    /// <summary>Single rows newest first, optionally one fingerprint or one trace id; page is 1-based.</summary>
    Task<AppLogPage> ListAsync(int hours, string? level, Guid? clinicId, string? search, string? fingerprint, string? traceId,
        int page, int pageSize, CancellationToken ct);

    /// <summary>One row in full. KeyNotFoundException (404) when it doesn't exist (or was purged).</summary>
    Task<AppLogDetail> GetAsync(long id, CancellationToken ct);

    /// <summary>Occurrences per level in the last <paramref name="hours"/> hours.</summary>
    Task<AppLogSummary> SummaryAsync(int hours, CancellationToken ct);
}
