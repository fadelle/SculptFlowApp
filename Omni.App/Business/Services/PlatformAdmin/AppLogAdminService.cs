using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Persistence.Contracts.Monitoring;

namespace Omni.App.Business.Services.PlatformAdmin;

public class AppLogAdminService : IAppLogAdminService
{
    public const string DefaultLevel = "warning";
    public const string DefaultSort = "recent";
    private static readonly string[] SortKeys = ["recent", "count"];

    private readonly IAppLogRepository _logs;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public AppLogAdminService(IAppLogRepository logs, IConfigManager config, TimeProvider time)
    {
        _logs = logs;
        _config = config;
        _time = time;
    }

    public async Task<AppLogGroupReport> GroupsAsync(int hours, string? level, Guid? clinicId, string? search, string? sort, int take, CancellationToken ct)
    {
        var sortKey = string.IsNullOrWhiteSpace(sort) ? DefaultSort : sort.Trim().ToLowerInvariant();
        if (!SortKeys.Contains(sortKey))
        {
            throw new ArgumentException($"Unknown sort '{sort}'. Use one of: {string.Join(", ", SortKeys)}.");
        }

        var (levelKey, filter, from, to, clampedHours) = Filter(hours, level, clinicId, search, null, null);
        take = Math.Clamp(take, 1, AppLogLimits.MaxGroupTake);
        var (groups, groupCount, total) = await _logs.ListGroupsReadOnlyAsync(filter, sortKey == "count", take, ct);

        return new AppLogGroupReport(from, to, clampedHours, levelKey, sortKey, _config.ErrorLogEnabled, _config.ErrorLogMinimumLevel,
            _config.ErrorLogWarningRetentionDays, _config.ErrorLogErrorRetentionDays, total, groupCount, groups);
    }

    public async Task<AppLogPage> ListAsync(int hours, string? level, Guid? clinicId, string? search, string? fingerprint, string? traceId,
        int page, int pageSize, CancellationToken ct)
    {
        var (_, filter, from, to, _) = Filter(hours, level, clinicId, search, fingerprint, traceId);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, AppLogLimits.MaxPageSize);
        var (rows, total) = await _logs.ListReadOnlyAsync(filter, (page - 1) * pageSize, pageSize, ct);
        return new AppLogPage(from, to, page, pageSize, total, rows);
    }

    public async Task<AppLogDetail> GetAsync(long id, CancellationToken ct)
    {
        var row = await _logs.GetReadOnlyAsync(id, ct)
                  ?? throw new KeyNotFoundException($"Log entry {id} was not found (it may be past retention).");
        return AppLogMapper.ToDetail(row);
    }

    public async Task<AppLogSummary> SummaryAsync(int hours, CancellationToken ct)
    {
        hours = Math.Clamp(hours, 1, AppLogLimits.MaxReportHours);
        var from = _time.GetUtcNow().AddHours(-hours);
        var counts = await _logs.CountByLevelSinceReadOnlyAsync(from, ct);
        long Count(string level) => counts.FirstOrDefault(c => c.Level == level)?.Occurrences ?? 0;
        var lastError = counts.Where(c => c.Level != AppLogLevels.Warning).Select(c => (DateTimeOffset?)c.LastAt).Max();
        return new AppLogSummary(hours, from, _config.ErrorLogEnabled,
            Count(AppLogLevels.Warning), Count(AppLogLevels.Error), Count(AppLogLevels.Critical), lastError);
    }

    private (string Level, AppLogFilter Filter, DateTimeOffset From, DateTimeOffset To, int Hours) Filter(
        int hours, string? level, Guid? clinicId, string? search, string? fingerprint, string? traceId)
    {
        var levelKey = string.IsNullOrWhiteSpace(level) ? DefaultLevel : level.Trim().ToLowerInvariant();
        var minimum = AppLogLevels.Parse(levelKey)
                      ?? throw new ArgumentException($"Unknown level '{level}'. Use one of: warning, error, critical.");

        hours = Math.Clamp(hours, 1, AppLogLimits.MaxReportHours);
        var to = _time.GetUtcNow();
        var from = to.AddHours(-hours);
        var filter = new AppLogFilter(from, AppLogLevels.AtOrAbove(minimum), clinicId,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim().ToLowerInvariant());
        return (levelKey, filter, from, to, hours);
    }
}
