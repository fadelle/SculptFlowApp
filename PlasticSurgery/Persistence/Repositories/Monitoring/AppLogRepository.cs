using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Persistence.Repositories.Monitoring;

public class AppLogRepository : IAppLogRepository
{
    private readonly ApplicationDbContext _db;

    public AppLogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void AddRange(IEnumerable<AppLog> rows) => _db.AppLogs.AddRange(rows);

    public Task<int> DeleteOlderThanAsync(IReadOnlyCollection<string> levels, DateTimeOffset cutoff, CancellationToken ct = default) =>
        _db.AppLogs.Where(l => levels.Contains(l.Level) && l.OccurredAt < cutoff).ExecuteDeleteAsync(ct);

    public async Task<(List<AppLogGroupRow> Groups, int GroupCount, long TotalOccurrences)> ListGroupsReadOnlyAsync(
        AppLogFilter filter, bool byCount, int take, CancellationToken ct = default)
    {
        var groups = Filtered(filter)
            .GroupBy(l => l.Fingerprint)
            .Select(g => new
            {
                Fingerprint = g.Key,
                Occurrences = g.Sum(l => (long)l.RepeatCount),
                FirstSeenAt = g.Min(l => l.OccurredAt),
                LastSeenAt = g.Max(l => l.OccurredAt),
                // Ids grow with inserts, so the highest id is the latest row (one cheap lookup below).
                LatestId = g.Max(l => l.Id)
            });

        var groupCount = await Filtered(filter).Select(l => l.Fingerprint).Distinct().CountAsync(ct);
        if (groupCount == 0) return ([], 0, 0);
        var totalOccurrences = await Filtered(filter).SumAsync(l => (long)l.RepeatCount, ct);

        var sorted = byCount
            ? groups.OrderByDescending(g => g.Occurrences).ThenByDescending(g => g.LastSeenAt)
            : groups.OrderByDescending(g => g.LastSeenAt).ThenByDescending(g => g.Occurrences);
        var top = await sorted.ThenBy(g => g.Fingerprint).Take(take).ToListAsync(ct);

        var latestIds = top.Select(g => g.LatestId).ToList();
        var latest = await _db.AppLogs.AsNoTracking()
            .Where(l => latestIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Level, l.Category, l.MessageTemplate, l.Message, l.ExceptionType, l.ClinicId })
            .ToDictionaryAsync(l => l.Id, ct);

        var rows = top.Where(g => latest.ContainsKey(g.LatestId)).Select(g =>
        {
            var row = latest[g.LatestId];
            return new AppLogGroupRow(g.Fingerprint, row.Level, row.Category, row.MessageTemplate, row.Message, row.ExceptionType,
                g.Occurrences, g.FirstSeenAt, g.LastSeenAt, g.LatestId, row.ClinicId);
        }).ToList();
        return (rows, groupCount, totalOccurrences);
    }

    public async Task<(List<AppLogRow> Rows, int Total)> ListReadOnlyAsync(AppLogFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var query = Filtered(filter);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id)
            .Skip(skip).Take(take)
            .Select(l => new AppLogRow(l.Id, l.OccurredAt, l.Level, l.Category, l.Message, l.ExceptionType, l.Fingerprint,
                l.RepeatCount, l.TraceId, l.ClinicId, l.ConversationId, l.MessageId, l.Method, l.Route))
            .ToListAsync(ct);
        return (rows, total);
    }

    public Task<AppLog?> GetReadOnlyAsync(long id, CancellationToken ct = default) =>
        _db.AppLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<List<AppLogLevelCount>> CountByLevelSinceReadOnlyAsync(DateTimeOffset from, CancellationToken ct = default) =>
        _db.AppLogs.AsNoTracking()
            .Where(l => l.OccurredAt >= from)
            .GroupBy(l => l.Level)
            .Select(g => new AppLogLevelCount(g.Key, g.Sum(l => (long)l.RepeatCount), g.Max(l => l.OccurredAt)))
            .ToListAsync(ct);

    private IQueryable<AppLog> Filtered(AppLogFilter filter)
    {
        var levels = filter.Levels.ToList();
        var query = _db.AppLogs.AsNoTracking().Where(l => l.OccurredAt >= filter.From && levels.Contains(l.Level));
        if (filter.ClinicId is { } clinicId) query = query.Where(l => l.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(filter.Fingerprint)) query = query.Where(l => l.Fingerprint == filter.Fingerprint);
        if (!string.IsNullOrWhiteSpace(filter.TraceId)) query = query.Where(l => l.TraceId == filter.TraceId);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(l => EF.Functions.ILike(l.Message, pattern, "\\")
                                     || EF.Functions.ILike(l.Category, pattern, "\\")
                                     || (l.ExceptionType != null && EF.Functions.ILike(l.ExceptionType, pattern, "\\")));
        }
        return query;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
