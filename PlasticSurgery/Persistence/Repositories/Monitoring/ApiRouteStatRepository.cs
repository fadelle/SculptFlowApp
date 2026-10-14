using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Persistence.Repositories.Monitoring;

public class ApiRouteStatRepository : IApiRouteStatRepository
{
    private readonly ApplicationDbContext _db;

    public ApiRouteStatRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void AddRange(IEnumerable<ApiRouteStat> rows) => _db.ApiRouteStats.AddRange(rows);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        _db.ApiRouteStats.Where(s => s.BucketStart < cutoff).ExecuteDeleteAsync(ct);

    public async Task<List<RouteTimingRow>> SummariseSinceReadOnlyAsync(DateTimeOffset from, CancellationToken ct = default)
    {
        var groups = await _db.ApiRouteStats.AsNoTracking()
            .Where(s => s.BucketStart >= from)
            .GroupBy(s => new { s.Method, s.Route })
            .Select(g => new
            {
                g.Key.Method,
                g.Key.Route,
                Requests = g.Sum(s => (long)s.RequestCount),
                Errors = g.Sum(s => (long)s.ErrorCount),
                ClientErrors = g.Sum(s => (long)s.ClientErrorCount),
                Slow = g.Sum(s => (long)s.SlowCount),
                TotalMs = g.Sum(s => s.TotalMs),
                WeightedP95 = g.Sum(s => s.P95Ms * s.RequestCount),
                MaxMs = g.Max(s => s.MaxMs),
                LastSeenAt = g.Max(s => s.BucketStart)
            })
            .ToListAsync(ct);

        return groups.Select(g => new RouteTimingRow(
                g.Method,
                g.Route,
                g.Requests,
                g.Errors,
                g.ClientErrors,
                g.Slow,
                g.Requests == 0 ? 0 : Math.Round(g.TotalMs / g.Requests, 1),
                g.Requests == 0 ? 0 : Math.Round(g.WeightedP95 / g.Requests, 1),
                Math.Round(g.MaxMs, 1),
                Math.Round(g.TotalMs / 1000, 1),
                g.LastSeenAt))
            .ToList();
    }
}
