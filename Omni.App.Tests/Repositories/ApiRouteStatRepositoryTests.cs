using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Monitoring;

namespace Omni.App.Tests.Repositories;

/// <summary>activity.api_route_stats: rows are global (no client), so each test uses its own route names and dates.</summary>
public class ApiRouteStatRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static ApiRouteStat Stat(DateTimeOffset bucket, string route, string method = "GET", int count = 10, int errors = 0, int clientErrors = 0,
        int slow = 0, double totalMs = 100, double p95 = 20, double max = 30) => new()
    {
        BucketStart = bucket, Method = method, Route = route, RequestCount = count, ErrorCount = errors, ClientErrorCount = clientErrors,
        SlowCount = slow, TotalMs = totalMs, P95Ms = p95, MaxMs = max, CreatedAt = bucket,
    };

    [PostgresFact]
    public async Task Rows_are_added_with_the_caller_save_and_summed_per_method_and_route()
    {
        var route = "/test/" + Guid.NewGuid().ToString("N")[..8];
        var t = new DateTimeOffset(2099, 1, 1, 10, 0, 0, TimeSpan.Zero);
        await using (var db = NewContext())
        {
            new ApiRouteStatRepository(db).AddRange(
            [
                Stat(t, route, count: 10, errors: 1, clientErrors: 2, slow: 1, totalMs: 1000, p95: 200, max: 400),
                Stat(t.AddMinutes(1), route, count: 30, errors: 2, totalMs: 600, p95: 40, max: 90),
                Stat(t.AddMinutes(1), route, method: "POST", count: 5, totalMs: 50, p95: 10, max: 12),
                Stat(t.AddMinutes(-10), route, count: 999) // before the period
            ]);
            Assert.Equal(4, await db.SaveChangesAsync());
        }

        await using var read = NewContext();
        var rows = (await new ApiRouteStatRepository(read).SummariseSinceReadOnlyAsync(t.AddMinutes(-1))).Where(r => r.Route == route).ToList();

        Assert.Equal(2, rows.Count);
        var get = rows.Single(r => r.Method == "GET");
        Assert.Equal((40L, 3L, 2L, 1L), (get.Requests, get.Errors, get.ClientErrors, get.SlowRequests));
        Assert.Equal(40, get.AvgMs);            // 1600 ms / 40 requests
        Assert.Equal(80, get.P95Ms);            // (200×10 + 40×30) / 40
        Assert.Equal(400, get.MaxMs);
        Assert.Equal(1.6, get.TotalSeconds);
        Assert.Equal(t.AddMinutes(1), get.LastSeenAt);
        Assert.Equal((5L, 10d), (rows.Single(r => r.Method == "POST").Requests, rows.Single(r => r.Method == "POST").AvgMs));
    }

    [PostgresFact]
    public async Task Delete_older_than_removes_only_rows_before_the_cutoff()
    {
        var route = "/old/" + Guid.NewGuid().ToString("N")[..8];
        var cutoff = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(Stat(cutoff.AddDays(-1), route), Stat(cutoff.AddMinutes(-1), route), Stat(cutoff, route));

        await using (var db = NewContext())
        {
            Assert.True(await new ApiRouteStatRepository(db).DeleteOlderThanAsync(cutoff) >= 2);
        }

        await using var read = NewContext();
        var left = await read.ApiRouteStats.AsNoTracking().Where(s => s.Route == route).ToListAsync();
        Assert.Equal(cutoff, Assert.Single(left).BucketStart);
    }
}
