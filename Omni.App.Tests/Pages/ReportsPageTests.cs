using Omni.App.Business.Contracts.Services.Reports;
using Omni.App.Entities.Responses.Reports;
using static Omni.App.Tests.Pages.PageKit;
using ReportsIndex = Omni.App.Pages.Reports.IndexModel;

namespace Omni.App.Tests.Pages;

/// <summary>The Reports page shows what the plan includes: nothing without reporting, the basic sections with it, and the
/// advanced ones (plus charged usage while billing is on) with advanced reporting.</summary>
public class ReportsPageTests
{
    private static readonly Client Glow = SomeClient(timezone: "Asia/Beirut");
    private readonly Mock<IReportService> _reports = new();
    private readonly Mock<IContactService> _contacts = new();

    public ReportsPageTests()
    {
        _reports.Setup(r => r.GetBasicAsync(Glow.Id, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<TimeZoneInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<BasicReportResponse>());
        _reports.Setup(r => r.GetAdvancedAsync(Glow.Id, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<TimeZoneInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<AdvancedReportResponse>());
        _contacts.Setup(c => c.ListStagesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private ReportsIndex Page(Client? client, Mock<IEntitlementService> plan, bool billing = true, string period = ReportPeriod.ThisMonth,
        DateOnly? from = null, DateOnly? to = null)
    {
        var settings = new Mock<IConfigManager>();
        settings.Setup(s => s.BillingEnabled).Returns(billing);
        return new ReportsIndex(ClientContext(client).Object, plan.Object, _reports.Object, _contacts.Object, settings.Object, TextKit.Localizer())
            { Period = period, From = from, To = to }.Attach();
    }

    [Fact]
    public async Task Without_a_client_nothing_is_loaded()
    {
        var page = Page(null, EntitlementKit.All());
        await page.OnGetAsync(default);
        Assert.False(page.ClientConfigured);
        _reports.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_plan_without_reporting_says_so_and_loads_no_reports()
    {
        var page = Page(Glow, EntitlementKit.Plan((EntitlementKeys.Campaigns, "true")));
        await page.OnGetAsync(default);
        Assert.True(page.ClientConfigured);
        Assert.False(page.ReportingIncluded);
        Assert.Null(page.Basic);
        _reports.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Basic_reporting_shows_the_basic_sections_only()
    {
        var page = Page(Glow, EntitlementKit.Plan((EntitlementKeys.Reporting, "true")));
        await page.OnGetAsync(default);
        Assert.True(page.ReportingIncluded);
        Assert.NotNull(page.Basic);
        Assert.Null(page.Advanced);
        Assert.False(page.ShowUsage);
        _reports.Verify(r => r.GetAdvancedAsync(It.IsAny<Guid>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<TimeZoneInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Advanced_reporting_adds_the_advanced_sections_and_usage_while_billing_is_on()
    {
        var plan = EntitlementKit.Plan((EntitlementKeys.Reporting, "true"), (EntitlementKeys.AdvancedReporting, "true"));
        var page = Page(Glow, plan, period: ReportPeriod.Custom, from: new DateOnly(2026, 3, 1), to: new DateOnly(2026, 3, 10));
        await page.OnGetAsync(default);

        Assert.NotNull(page.Advanced);
        Assert.True(page.ShowUsage);
        Assert.Equal("Mar 1 – Mar 10, 2026", page.RangeLabel);
        // Without the browser's timezone cookie the client's zone is the viewer's.
        _reports.Verify(r => r.GetAdvancedAsync(Glow.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10),
            It.Is<TimeZoneInfo>(tz => tz.Id == "Asia/Beirut"), It.IsAny<CancellationToken>()));

        var billingOff = Page(Glow, plan, billing: false);
        await billingOff.OnGetAsync(default);
        Assert.NotNull(billingOff.Advanced);
        Assert.False(billingOff.ShowUsage);
    }

    [Fact]
    public async Task An_unknown_period_falls_back_to_this_month()
    {
        var page = Page(Glow, EntitlementKit.All(), period: "forever");
        await page.OnGetAsync(default);
        Assert.Equal(ReportPeriod.ThisMonth, page.Period);
    }
}
