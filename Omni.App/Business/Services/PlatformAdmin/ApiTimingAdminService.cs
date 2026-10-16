using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Persistence.Contracts.Monitoring;

namespace Omni.App.Business.Services.PlatformAdmin;

public class ApiTimingAdminService : IApiTimingAdminService
{
    public const string DefaultSort = "slowest";

    // Every sort is descending; ties fall back to call count, then route, so the order is stable.
    private static readonly Dictionary<string, Func<RouteTimingRow, double>> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slowest"] = r => r.AvgMs,
        ["p95"] = r => r.P95Ms,
        ["max"] = r => r.MaxMs,
        ["calls"] = r => r.Requests,
        ["errors"] = r => r.Errors,
        ["slow"] = r => r.SlowRequests,
        ["time"] = r => r.TotalSeconds
    };

    private readonly IApiRouteStatRepository _stats;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public ApiTimingAdminService(IApiRouteStatRepository stats, IConfigManager config, TimeProvider time)
    {
        _stats = stats;
        _config = config;
        _time = time;
    }

    public async Task<ApiTimingReport> ReportAsync(int hours, string? sort, int take, CancellationToken ct)
    {
        var sortKey = string.IsNullOrWhiteSpace(sort) ? DefaultSort : sort.Trim().ToLowerInvariant();
        if (!Sorts.TryGetValue(sortKey, out var by))
        {
            throw new ArgumentException($"Unknown sort '{sort}'. Use one of: {string.Join(", ", Sorts.Keys)}.");
        }

        hours = Math.Clamp(hours, 1, ApiTimingLimits.MaxReportHours);
        take = Math.Clamp(take, 1, ApiTimingLimits.MaxReportTake);
        var to = _time.GetUtcNow();
        var from = to.AddHours(-hours);

        var all = await _stats.SummariseSinceReadOnlyAsync(from, ct);
        var routes = all.OrderByDescending(by)
            .ThenByDescending(r => r.Requests)
            .ThenBy(r => r.Route, StringComparer.Ordinal)
            .ThenBy(r => r.Method, StringComparer.Ordinal)
            .Take(take)
            .ToList();

        return new ApiTimingReport(
            from,
            to,
            hours,
            sortKey,
            _config.MonitoringEnabled,
            _config.MonitoringSlowRequestMs,
            _config.MonitoringRetentionDays,
            all.Sum(r => r.Requests),
            all.Sum(r => r.Errors),
            all.Sum(r => r.SlowRequests),
            all.Count,
            routes);
    }
}
