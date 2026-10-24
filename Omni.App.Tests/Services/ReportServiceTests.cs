using Omni.App.Business.Services.Reports;
using Omni.App.Entities.Dtos.Reports;
using Omni.App.Entities.Responses.Reports;
using Omni.App.Persistence.Contracts.Reports;

namespace Omni.App.Tests.Services;

/// <summary>ReportService: plan gating (reporting / advanced_reporting, bookings and campaigns sections), the viewer-local
/// period sent to the queries, response-time maths and folding message slots into days or months.</summary>
public class ReportServiceTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly Mock<IReportRepository> _repo = new();

    public ReportServiceTests()
    {
        _repo.Setup(r => r.GetChannelActivityAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReportChannelRow("whatsapp", 3, 10, 7), new ReportChannelRow("telegram", 1, 2, 1)]);
        _repo.Setup(r => r.CountNewContactsBySourceAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReportCountRow("instagram", 4), new ReportCountRow(null, 1)]);
        _repo.Setup(r => r.CountNewContactsByStageAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReportCountRow("new", 5)]);
        _repo.Setup(r => r.GetBookingCountsAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportBookingCounts(2, 1, 0, 1));
        _repo.Setup(r => r.ListCampaignsAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.ListWaitsAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.GetReplyCountsAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportReplyCounts(4, 2, 2, 1));
        _repo.Setup(r => r.GetMessageBucketsAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.GetSettledUsageAsync(ClientId, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private ReportService Service(params (string Key, string Value)[] plan) => new(_repo.Object, EntitlementKit.Plan(plan).Object);

    [Fact]
    public async Task Basic_needs_the_reporting_feature()
    {
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Service().GetBasicAsync(ClientId, null, null, Utc));
        _repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Basic_totals_the_channels_and_sources_and_leaves_out_what_the_plan_lacks()
    {
        var report = await Service((EntitlementKeys.Reporting, "true")).GetBasicAsync(ClientId, null, null, Utc);

        Assert.Equal((4, 12, 8, 5), (report.NewConversations, report.MessagesReceived, report.MessagesSent, report.NewContacts));
        Assert.Null(report.Bookings);
        Assert.Null(report.Campaigns);
        _repo.Verify(r => r.GetBookingCountsAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Basic_includes_bookings_and_campaigns_when_the_plan_has_them_and_sends_local_midnights()
    {
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var report = await Service((EntitlementKeys.Reporting, "true"), (EntitlementKeys.Bookings, "true"), (EntitlementKeys.Campaigns, "true"))
            .GetBasicAsync(ClientId, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 11), beirut);

        Assert.Equal(new ReportBookingCounts(2, 1, 0, 1), report.Bookings);
        Assert.NotNull(report.Campaigns);
        // Beirut is UTC+2 in January: local midnights are 22:00 UTC the evening before; the end is the midnight after the 11th.
        _repo.Verify(r => r.ListCampaignsAsync(ClientId, new DateTimeOffset(2026, 1, 9, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 11, 22, 0, 0, TimeSpan.Zero), ReportLimits.CampaignRows, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Advanced_needs_advanced_reporting()
    {
        await Assert.ThrowsAsync<EntitlementDeniedException>(() =>
            Service((EntitlementKeys.Reporting, "true")).GetAdvancedAsync(ClientId, null, null, Utc));
    }

    [Fact]
    public async Task Advanced_shows_short_periods_by_day_and_long_or_open_ones_by_month()
    {
        var service = Service((EntitlementKeys.AdvancedReporting, "true"));

        var week = await service.GetAdvancedAsync(ClientId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7), Utc);
        Assert.False(week.ActivityByMonth);
        Assert.Equal(7, week.Activity.Count);          // every day, empty ones too
        Assert.Equal(new ReportReplyCounts(4, 2, 2, 1), week.Replies);

        var quarter = await service.GetAdvancedAsync(ClientId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), Utc);
        Assert.True(quarter.ActivityByMonth);
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)], quarter.Activity.Select(a => a.Start));

        var allTime = await service.GetAdvancedAsync(ClientId, null, null, Utc);
        Assert.True(allTime.ActivityByMonth);
        Assert.Empty(allTime.Activity);                // nothing yet, and no period to fill
    }

    [Fact]
    public void Response_times_take_the_median_average_and_fast_share_overall_and_by_who_answered()
    {
        var t0 = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
        var stats = ReportService.ResponseTimes(
        [
            new ReportWait(t0, t0.AddMinutes(1), true),
            new ReportWait(t0, t0.AddMinutes(3), true),
            new ReportWait(t0, t0.AddMinutes(20), false),
            new ReportWait(t0, t0.AddMinutes(60), false),
            new ReportWait(t0, null, false),
        ]);

        Assert.Equal((4, 1, 2), (stats.Answered, stats.Unanswered, stats.WithinFiveMinutes));
        Assert.Equal(TimeSpan.FromMinutes(11.5), stats.Median);      // between 3 and 20
        Assert.Equal(TimeSpan.FromMinutes(21), stats.Average);
        Assert.Equal(TimeSpan.FromMinutes(2), stats.AiMedian);
        Assert.Equal(TimeSpan.FromMinutes(40), stats.TeamMedian);

        var none = ReportService.ResponseTimes([new ReportWait(t0, null, false)]);
        Assert.Equal((0, 1, null, null, null, null), (none.Answered, none.Unanswered, none.Median, none.Average, none.AiMedian, none.TeamMedian));
    }

    [Fact]
    public void Activity_folds_utc_slots_into_the_viewers_days()
    {
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");   // UTC+3 in June
        var rows = ReportService.Activity(
        [
            new ReportMessageBucket(new DateTimeOffset(2026, 6, 1, 20, 45, 0, TimeSpan.Zero), 2, 1),   // 23:45 on Jun 1
            new ReportMessageBucket(new DateTimeOffset(2026, 6, 1, 21, 0, 0, TimeSpan.Zero), 1, 4),    // 00:00 on Jun 2
            new ReportMessageBucket(new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero), 3, 0),
        ], new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), beirut, byMonth: false);

        Assert.Equal(
            [
                new ReportActivityRow(new DateOnly(2026, 6, 1), 2, 1),
                new ReportActivityRow(new DateOnly(2026, 6, 2), 4, 4),
                new ReportActivityRow(new DateOnly(2026, 6, 3), 0, 0),
            ],
            rows);
    }

    [Fact]
    public void Open_ended_activity_by_month_runs_from_the_first_to_the_last_month_with_messages()
    {
        var rows = ReportService.Activity(
        [
            new ReportMessageBucket(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), 1, 0),
            new ReportMessageBucket(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero), 0, 2),
        ], null, null, TimeZoneInfo.Utc, byMonth: true);

        Assert.Equal(
            [
                new ReportActivityRow(new DateOnly(2026, 1, 1), 1, 0),
                new ReportActivityRow(new DateOnly(2026, 2, 1), 0, 0),
                new ReportActivityRow(new DateOnly(2026, 3, 1), 0, 2),
            ],
            rows);
    }
}
