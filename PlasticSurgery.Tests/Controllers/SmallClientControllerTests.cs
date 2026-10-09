using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Controllers.Client;

namespace PlasticSurgery.Tests.Controllers;

public class SmallClientControllerTests : ClientControllerTestBase
{
    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    // ---- dashboard --------------------------------------------------------------------------------------------

    private readonly Mock<IDashboardService> _dashboard = new();
    private DashboardController Dashboard() => new DashboardController(_dashboard.Object, Clinic.Object).With();

    [Fact]
    public async Task Dashboard_summary_attention_and_procedures_use_the_signed_in_clinic()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;
        _dashboard.Setup(d => d.GetSummaryAsync(ClinicId, from, to, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<DashboardSummaryResponse>()).Verifiable();
        _dashboard.Setup(d => d.GetAttentionAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<DashboardAttentionResponse>()).Verifiable();
        _dashboard.Setup(d => d.GetProcedureStatsAsync(ClinicId, from, to, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DashboardProcedureRow>()).Verifiable();
        var sut = Dashboard();
        Assert.IsType<OkObjectResult>((await sut.Summary(from, to, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.Attention(default)).Result);
        Assert.IsType<OkObjectResult>((await sut.Procedures(from, to, default)).Result);
        _dashboard.Verify();
    }

    [Fact]
    public async Task Dashboard_lists_clamp_take_to_1_200()
    {
        _dashboard.Setup(d => d.GetLeadsAsync(ClinicId, "new", "q", "web", 3, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DashboardLeadRow>() as IReadOnlyList<DashboardLeadRow>, 7)).Verifiable();
        _dashboard.Setup(d => d.GetAppointmentsAsync(ClinicId, "booked", null, 0, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DashboardAppointmentRow>() as IReadOnlyList<DashboardAppointmentRow>, 2)).Verifiable();
        var sut = Dashboard();
        var leads = Assert.IsType<OkObjectResult>((await sut.Leads("new", "q", "web", 3, 5000, default)).Result);
        Assert.Contains("totalCount = 7", leads.Value!.ToString());
        Assert.Contains("take = 200", leads.Value!.ToString());
        var appts = Assert.IsType<OkObjectResult>((await sut.Appointments("booked", null, 0, 0, default)).Result);
        Assert.Contains("take = 1", appts.Value!.ToString());
        _dashboard.Verify();
    }

    // ---- notifications ----------------------------------------------------------------------------------------

    private readonly Mock<INotificationService> _notifications = new();
    private NotificationsController Notifications() => new NotificationsController(_notifications.Object, Clinic.Object).With();

    [Fact]
    public async Task Notifications_list_clamps_take_and_reports_unread_count()
    {
        _notifications.Setup(n => n.ListAsync(ClinicId, 4, 100, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<NotificationListResponse>()).Verifiable();
        _notifications.Setup(n => n.GetUnreadCountAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(9);
        var sut = Notifications();
        Assert.IsType<OkObjectResult>((await sut.List(4, 5000, default)).Result);
        var unread = Assert.IsType<OkObjectResult>((await sut.UnreadCount(default)).Result);
        Assert.Contains("count = 9", unread.Value!.ToString());
        _notifications.Verify();
    }

    [Fact]
    public async Task Notifications_mark_read_is_scoped_to_the_clinic_and_returns_204()
    {
        var id = Guid.NewGuid();
        var sut = Notifications();
        Assert.IsType<NoContentResult>(await sut.MarkRead(id, default));
        Assert.IsType<NoContentResult>(await sut.MarkAllRead(default));
        _notifications.Verify(n => n.MarkReadAsync(ClinicId, id, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.MarkAllReadAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- whatsapp templates -----------------------------------------------------------------------------------

    private readonly Mock<IWhatsAppTemplateService> _templates = new();
    private WhatsAppTemplatesController Templates() => new WhatsAppTemplatesController(_templates.Object, Clinic.Object).With();

    [Fact]
    public async Task Template_get_and_sync_are_404_when_missing()
    {
        var sut = Templates();
        Assert.IsType<NotFoundResult>((await sut.GetById(Guid.NewGuid(), default)).Result);
        Assert.IsType<NotFoundResult>((await sut.Sync(Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task Template_get_sync_and_list_return_the_service_result()
    {
        var id = Guid.NewGuid();
        var template = Blank<WhatsAppTemplateResponse>();
        _templates.Setup(t => t.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppTemplateResponse> { template });
        _templates.Setup(t => t.GetByIdAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _templates.Setup(t => t.SyncStatusAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        var sut = Templates();
        Assert.IsType<OkObjectResult>((await sut.List(default)).Result);
        Assert.Same(template, Ok(await sut.GetById(id, default)));
        Assert.Same(template, Ok(await sut.Sync(id, default)));
    }

    [Fact]
    public async Task Template_create_takes_the_clinic_from_the_signed_in_user_not_the_body()
    {
        var spoofed = Guid.NewGuid();
        var request = Blank<CreateWhatsAppTemplateRequest>() with { ClinicId = spoofed };
        CreateWhatsAppTemplateRequest? seen = null;
        _templates.Setup(t => t.CreateAsync(It.IsAny<CreateWhatsAppTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateWhatsAppTemplateRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<WhatsAppTemplateResponse>());
        var result = await Templates().Create(request, default);
        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(ClinicId, seen!.ClinicId);
    }

    // ---- calendar integrations --------------------------------------------------------------------------------

    private readonly Mock<ICalendarIntegrationService> _calendar = new();
    private CalendarIntegrationsController Calendar() => new CalendarIntegrationsController(_calendar.Object, Clinic.Object).With();

    [Fact]
    public async Task Calendar_list_select_toggle_and_disconnect_are_scoped_to_the_clinic()
    {
        _calendar.Setup(c => c.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegrationResponse>());
        _calendar.Setup(c => c.SelectCalendarAsync(ClinicId, "google", "cal-1", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<CalendarIntegrationResponse>());
        _calendar.Setup(c => c.SetSyncEnabledAsync(ClinicId, "google", true, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<CalendarIntegrationResponse>());
        var sut = Calendar();
        Assert.IsType<OkObjectResult>((await sut.List(default)).Result);
        Assert.IsType<OkObjectResult>((await sut.SelectCalendar("google", new SelectCalendarRequest("cal-1"), default)).Result);
        Assert.IsType<OkObjectResult>((await sut.SetSyncEnabled("google", new SetCalendarSyncEnabledRequest(true), default)).Result);
        Assert.IsType<NoContentResult>(await sut.Disconnect("google", default));
        _calendar.Verify(c => c.DisconnectAsync(ClinicId, "google", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Calendar_refresh_is_204_on_success()
    {
        Assert.IsType<NoContentResult>(await Calendar().RefreshCalendars("google", default));
        _calendar.Verify(c => c.RequestRefreshCalendarsAsync(ClinicId, "google", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Calendar_refresh_maps_both_domain_errors_to_400(bool argument)
    {
        Exception ex = argument ? new ArgumentException("bad provider") : new InvalidOperationException("not connected");
        _calendar.Setup(c => c.RequestRefreshCalendarsAsync(ClinicId, "x", It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        var result = await Calendar().RefreshCalendars("x", default);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(ex.Message, bad.Value!.ToString());
    }

    // ---- whatsapp health --------------------------------------------------------------------------------------

    private readonly Mock<IWhatsAppHealthService> _health = new();
    private WhatsAppHealthController Health() => new WhatsAppHealthController(_health.Object, Clinic.Object).With();

    [Fact]
    public async Task Health_is_404_when_no_whatsapp_connection_exists()
    {
        Assert.IsType<NotFoundResult>((await Health().GetHealth(default)).Result);
    }

    [Fact]
    public async Task Health_returns_the_status_and_clamps_the_events_page()
    {
        _health.Setup(h => h.GetHealthAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WhatsAppHealthResponse>());
        _health.Setup(h => h.GetHealthEventsAsync(ClinicId, 2, 200, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEventResponse>()).Verifiable();
        var sut = Health();
        Assert.IsType<OkObjectResult>((await sut.GetHealth(default)).Result);
        Assert.IsType<OkObjectResult>((await sut.GetHealthEvents(2, 99999, default)).Result);
        _health.Verify();
    }

    // ---- billing (clinic) -------------------------------------------------------------------------------------

    private readonly Mock<IBillingQueryService> _billing = new();
    private BillingController Billing() => new BillingController(Clinic.Object, _billing.Object).With();

    [Fact]
    public async Task Clinic_billing_reads_use_the_signed_in_clinic()
    {
        _billing.Setup(b => b.GetSummaryAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ClinicBillingSummary>()).Verifiable();
        _billing.Setup(b => b.ListClinicUsageAsync(ClinicId, 2, 25, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<PagedResponse<ClinicUsageRow>>()).Verifiable();
        _billing.Setup(b => b.ListTransactionsAsync(ClinicId, 3, 10, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<PagedResponse<BillingTransactionRow>>()).Verifiable();
        var sut = Billing();
        Assert.IsType<OkObjectResult>((await sut.Summary(default)).Result);
        Assert.IsType<OkObjectResult>((await sut.Usage(2, 25, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.Transactions(3, 10, default)).Result);
        _billing.Verify();
    }

    // ---- staff ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Staff_list_is_scoped_to_the_clinic()
    {
        var staff = new Mock<IStaffService>();
        staff.Setup(s => s.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<StaffMemberResponse>());
        Assert.IsType<OkObjectResult>((await new StaffController(staff.Object, Clinic.Object).With().List(default)).Result);
    }
}
