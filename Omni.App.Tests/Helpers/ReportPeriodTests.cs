namespace Omni.App.Tests.Helpers;

/// <summary>The Dashboard and Reports period picker: what each choice covers, in viewer-local dates.</summary>
public class ReportPeriodTests
{
    private static readonly DateOnly Wednesday = new(2026, 4, 15);

    [Theory]
    [InlineData(ReportPeriod.ThisWeek, "2026-04-13", "2026-04-15")]
    [InlineData(ReportPeriod.LastWeek, "2026-04-06", "2026-04-12")]
    [InlineData(ReportPeriod.ThisMonth, "2026-04-01", "2026-04-15")]
    [InlineData(ReportPeriod.LastMonth, "2026-03-01", "2026-03-31")]
    [InlineData(ReportPeriod.Last7Days, "2026-04-09", "2026-04-15")]
    [InlineData(ReportPeriod.Last30Days, "2026-03-17", "2026-04-15")]
    public void Each_preset_covers_its_days(string period, string from, string to)
    {
        Assert.Equal((period, (DateOnly?)DateOnly.Parse(from), (DateOnly?)DateOnly.Parse(to)), ReportPeriod.Resolve(period, null, null, Wednesday));
    }

    [Fact]
    public void All_time_and_custom_ranges()
    {
        Assert.Equal((ReportPeriod.AllTime, (DateOnly?)null, (DateOnly?)null), ReportPeriod.Resolve(ReportPeriod.AllTime, null, null, Wednesday));
        Assert.Equal((ReportPeriod.Custom, (DateOnly?)new DateOnly(2026, 4, 1), (DateOnly?)Wednesday),
            ReportPeriod.Resolve(ReportPeriod.Custom, new DateOnly(2026, 4, 1), null, Wednesday));           // open end = today
        Assert.Equal((ReportPeriod.Custom, (DateOnly?)new DateOnly(2026, 4, 2), (DateOnly?)new DateOnly(2026, 4, 2)),
            ReportPeriod.Resolve(ReportPeriod.Custom, null, new DateOnly(2026, 4, 2), Wednesday));           // only an end = that day
        Assert.Equal(ReportPeriod.ThisMonth, ReportPeriod.Resolve("nonsense", null, null, Wednesday).Period);
    }

    [Fact]
    public void Utc_range_runs_from_local_midnight_to_the_midnight_after_the_last_day()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");   // UTC-4 in April
        var (from, to) = ReportPeriod.ToUtcRange(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 2), newYork);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 4, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 4, 3, 4, 0, 0, TimeSpan.Zero), to);
        Assert.Equal((null, null), ReportPeriod.ToUtcRange(null, null, newYork));
    }
}
