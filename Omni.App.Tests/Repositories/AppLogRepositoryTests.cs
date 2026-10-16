using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Monitoring;

namespace Omni.App.Tests.Repositories;

/// <summary>activity.app_logs: rows are global (no clinic FK), so each test uses its own fingerprints, categories and dates.</summary>
public class AppLogRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..12];

    private static AppLog Log(DateTimeOffset at, string fingerprint, string category, string level = "Error", string message = "m",
        int repeat = 1, Guid? clinicId = null, string? traceId = null, string? exceptionType = null) => new()
    {
        OccurredAt = at, Level = level, Category = category, Message = message, MessageTemplate = "t", Fingerprint = fingerprint,
        RepeatCount = repeat, ClinicId = clinicId, TraceId = traceId, ExceptionType = exceptionType, Properties = "{\"A\":\"1\"}"
    };

    [PostgresFact]
    public async Task Groups_sum_repeats_keep_the_latest_text_and_sort_by_recent_or_count()
    {
        var tag = Tag();
        var t = new DateTimeOffset(2099, 2, 1, 10, 0, 0, TimeSpan.Zero);
        var a = tag[..8] + "a";
        var b = tag[..8] + "b";
        await SeedAsync(
            Log(t, a, tag, message: "first a"),
            Log(t.AddMinutes(1), a, tag, message: "latest a", repeat: 9),
            Log(t.AddMinutes(5), b, tag, level: "Critical", message: "only b"),
            Log(t.AddMinutes(6), b, tag, level: "Warning", message: "warning b"),
            Log(t.AddDays(-2), a, tag, message: "before the period"));

        await using var db = NewContext();
        var repo = new AppLogRepository(db);
        var filter = new AppLogFilter(t.AddMinutes(-1), ["Error", "Critical"], Search: tag);

        var (recent, count, total) = await repo.ListGroupsReadOnlyAsync(filter, byCount: false, take: 10);
        Assert.Equal((2, 11L), (count, total));
        Assert.Equal([b, a], recent.Select(g => g.Fingerprint));
        var groupA = recent[1];
        Assert.Equal(("latest a", 10L, t, t.AddMinutes(1), "Error"), (groupA.LatestMessage, groupA.Occurrences, groupA.FirstSeenAt, groupA.LastSeenAt, groupA.Level));
        Assert.Equal(("only b", "Critical"), (recent[0].LatestMessage, recent[0].Level)); // the warning row is outside the level filter

        var (byCount, _, _) = await repo.ListGroupsReadOnlyAsync(filter, byCount: true, take: 1);
        Assert.Equal(a, Assert.Single(byCount).Fingerprint);

        var (none, noneCount, noneTotal) = await repo.ListGroupsReadOnlyAsync(filter with { Search = "nothing-" + tag }, false, 10);
        Assert.Equal((0, 0, 0L), (none.Count, noneCount, noneTotal));
    }

    [PostgresFact]
    public async Task List_filters_by_clinic_trace_fingerprint_and_search_and_pages_newest_first()
    {
        var tag = Tag();
        var clinicId = Guid.NewGuid();
        var trace = Guid.NewGuid().ToString("N");
        var t = new DateTimeOffset(2099, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var fp = tag[..9];
        await SeedAsync(
            Log(t, fp, tag, clinicId: clinicId, message: "one 100%_done"),
            Log(t.AddMinutes(1), fp, tag, clinicId: clinicId, traceId: trace, message: "two"),
            Log(t.AddMinutes(2), fp, tag, message: "three", exceptionType: "System.TimeoutException"),
            Log(t.AddMinutes(3), tag[..8] + "zz", tag, message: "four"));

        await using var db = NewContext();
        var repo = new AppLogRepository(db);
        var all = new AppLogFilter(t.AddMinutes(-1), AppLogLevels.All, Search: tag);

        var (page1, total) = await repo.ListReadOnlyAsync(all, 0, 2);
        Assert.Equal(4, total);
        Assert.Equal(["four", "three"], page1.Select(r => r.Message));
        var (page2, _) = await repo.ListReadOnlyAsync(all, 2, 2);
        Assert.Equal(["two", "one 100%_done"], page2.Select(r => r.Message));

        Assert.Equal(2, (await repo.ListReadOnlyAsync(all with { ClinicId = clinicId }, 0, 10)).Total);
        Assert.Equal("two", Assert.Single((await repo.ListReadOnlyAsync(all with { TraceId = trace }, 0, 10)).Rows).Message);
        Assert.Equal(3, (await repo.ListReadOnlyAsync(all with { Fingerprint = fp }, 0, 10)).Total);
        // Search covers the exception type and treats % and _ literally.
        Assert.Equal("three", Assert.Single((await repo.ListReadOnlyAsync(new AppLogFilter(t.AddMinutes(-1), AppLogLevels.All, Search: "timeoutexception", Fingerprint: fp), 0, 10)).Rows).Message);
        Assert.Equal("one 100%_done", Assert.Single((await repo.ListReadOnlyAsync(new AppLogFilter(t.AddMinutes(-1), AppLogLevels.All, Search: "100%_d", Fingerprint: fp), 0, 10)).Rows).Message);
        Assert.Empty((await repo.ListReadOnlyAsync(new AppLogFilter(t.AddMinutes(-1), AppLogLevels.All, Search: "1_0", Fingerprint: fp), 0, 10)).Rows);
        Assert.Empty((await repo.ListReadOnlyAsync(all with { Levels = ["Warning"] }, 0, 10)).Rows);

        var row = await repo.GetReadOnlyAsync(page1[0].Id);
        Assert.Equal(("four", "{\"A\": \"1\"}"), (row!.Message, row.Properties));
        Assert.Null(await repo.GetReadOnlyAsync(-1));
    }

    [PostgresFact]
    public async Task Rows_are_added_with_the_caller_save_and_counted_per_level()
    {
        var tag = Tag();
        var t = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero); // later than any other test's rows
        await using (var db = NewContext())
        {
            new AppLogRepository(db).AddRange([Log(t, tag, tag, "Warning", repeat: 3), Log(t.AddMinutes(1), tag, tag, "Error"), Log(t.AddMinutes(2), tag, tag, "Error")]);
            Assert.Equal(3, await db.SaveChangesAsync());
        }

        await using var read = NewContext();
        var counts = await new AppLogRepository(read).CountByLevelSinceReadOnlyAsync(t);
        Assert.Equal((3L, t), (counts.Single(c => c.Level == "Warning").Occurrences, counts.Single(c => c.Level == "Warning").LastAt));
        Assert.Equal((2L, t.AddMinutes(2)), (counts.Single(c => c.Level == "Error").Occurrences, counts.Single(c => c.Level == "Error").LastAt));
        Assert.DoesNotContain(counts, c => c.Level == "Critical");
    }

    [PostgresFact]
    public async Task Delete_older_than_removes_only_the_given_levels_before_the_cutoff()
    {
        var tag = Tag();
        var cutoff = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            Log(cutoff.AddDays(-1), tag, tag, "Warning"),
            Log(cutoff.AddDays(-1), tag, tag, "Error"),
            Log(cutoff, tag, tag, "Warning"));

        await using (var db = NewContext())
        {
            Assert.True(await new AppLogRepository(db).DeleteOlderThanAsync(["Warning"], cutoff) >= 1);
        }

        await using var read = NewContext();
        var left = await read.AppLogs.AsNoTracking().Where(l => l.Category == tag).OrderBy(l => l.Level).ToListAsync();
        Assert.Equal(["Error", "Warning"], left.Select(l => l.Level));
        Assert.Equal(cutoff, left.Single(l => l.Level == "Warning").OccurredAt);
    }
}
