using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Entities.Responses.Procedures;
using static PlasticSurgery.Tests.Pages.PageKit;
using DashboardIndex = PlasticSurgery.Pages.Dashboard.IndexModel;
using ProcedureIndex = PlasticSurgery.Pages.Procedures.IndexModel;
using ProcedureEdit = PlasticSurgery.Pages.Procedures.EditModel;
using StaffIndex = PlasticSurgery.Pages.Staff.IndexModel;
using InboxIndex = PlasticSurgery.Pages.Inbox.IndexModel;
using CampaignIndex = PlasticSurgery.Pages.Campaigns.IndexModel;
using CampaignDetails = PlasticSurgery.Pages.Campaigns.DetailsModel;
using HealthIndex = PlasticSurgery.Pages.WhatsApp.Health.IndexModel;

namespace PlasticSurgery.Tests.Pages;

/// <summary>"No clinic yet" must be a harmless empty page for every list page, never an exception.</summary>
public class SimpleListPageTests
{
    private static readonly Clinic Glow = SomeClinic();

    [Fact]
    public async Task Staff_page_lists_members_and_knows_the_current_user()
    {
        var staff = new Mock<IStaffService>();
        staff.Setup(s => s.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<StaffMemberResponse> { Blank<StaffMemberResponse>() });
        var model = new StaffIndex(ClinicContext(Glow).Object, staff.Object).Attach(UserWithId("user-1"));
        await model.OnGetAsync(default);
        Assert.True(model.ClinicConfigured);
        Assert.Equal(("Glow Clinic", "user-1"), (model.ClinicName, model.CurrentUserId));
        Assert.Single(model.Members);

        var empty = new StaffIndex(ClinicContext(null).Object, staff.Object).Attach();
        await empty.OnGetAsync(default);
        Assert.False(empty.ClinicConfigured);
        Assert.Empty(empty.Members);
    }

    [Fact]
    public async Task Inbox_loads_the_first_fifty_conversations()
    {
        var conversations = new Mock<IConversationService>();
        conversations.Setup(c => c.ListAsync(Glow.Id, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ConversationListRow>() as IReadOnlyList<ConversationListRow>, 0)).Verifiable();
        var model = new InboxIndex(ClinicContext(Glow).Object, conversations.Object).Attach();
        await model.OnGetAsync(default);
        Assert.Equal((true, Glow.Id), (model.ClinicConfigured, model.ClinicId));
        conversations.Verify();

        var none = new InboxIndex(ClinicContext(null).Object, conversations.Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public async Task Campaign_list_whatsapp_health_and_billing_pages_load_for_a_clinic_and_skip_without_one()
    {
        var campaigns = new Mock<ICampaignService>();
        campaigns.Setup(c => c.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignListRow>());
        var campaignPage = new CampaignIndex(ClinicContext(Glow).Object, campaigns.Object).Attach();
        await campaignPage.OnGetAsync(default);
        Assert.True(campaignPage.ClinicConfigured);
        var noCampaigns = new CampaignIndex(ClinicContext(null).Object, campaigns.Object).Attach();
        await noCampaigns.OnGetAsync(default);
        Assert.False(noCampaigns.ClinicConfigured);

        var health = new Mock<IWhatsAppHealthService>();
        health.Setup(h => h.GetHealthAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WhatsAppHealthResponse>());
        health.Setup(h => h.GetHealthEventsAsync(Glow.Id, 0, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEventResponse>());
        var healthPage = new HealthIndex(ClinicContext(Glow).Object, health.Object).Attach();
        await healthPage.OnGetAsync(default);
        Assert.Equal((true, Glow.Id), (healthPage.ClinicConfigured, healthPage.ClinicId));
        Assert.NotNull(healthPage.Health);
        var noHealth = new HealthIndex(ClinicContext(null).Object, health.Object).Attach();
        await noHealth.OnGetAsync(default);
        Assert.False(noHealth.ClinicConfigured);

        var billing = new Mock<IBillingQueryService>();
        billing.Setup(b => b.GetSummaryAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ClinicBillingSummary>());
        var billingPage = new PlasticSurgery.Pages.Settings.BillingModel(ClinicContext(Glow).Object, billing.Object).Attach();
        await billingPage.OnGetAsync(default);
        Assert.True(billingPage.ClinicConfigured);
        Assert.NotNull(billingPage.Summary);
        var noBilling = new PlasticSurgery.Pages.Settings.BillingModel(ClinicContext(null).Object, billing.Object).Attach();
        await noBilling.OnGetAsync(default);
        Assert.False(noBilling.ClinicConfigured);
    }

    [Fact]
    public async Task Appointments_and_benchmark_pages_only_need_the_clinic()
    {
        var appointments = new PlasticSurgery.Pages.Dashboard.AppointmentsModel(ClinicContext(SomeClinic(timezone: "Asia/Beirut")).Object).Attach();
        await appointments.OnGetAsync(default);
        Assert.Equal(("Asia/Beirut", true), (appointments.ClinicTimezone, appointments.ClinicConfigured));
        var blankZone = new PlasticSurgery.Pages.Dashboard.AppointmentsModel(ClinicContext(SomeClinic(timezone: " ")).Object).Attach();
        await blankZone.OnGetAsync(default);
        Assert.Equal("UTC", blankZone.ClinicTimezone);
        var none = new PlasticSurgery.Pages.Dashboard.AppointmentsModel(ClinicContext(null).Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClinicConfigured);

        var benchmark = new PlasticSurgery.Pages.KnowledgeBase.BenchmarkModel(ClinicContext(Glow).Object).Attach();
        await benchmark.OnGetAsync(default);
        Assert.True(benchmark.ClinicConfigured);
        var generation = new PlasticSurgery.Pages.KnowledgeBase.Benchmark.GenerationModel(ClinicContext(null).Object).Attach();
        await generation.OnGetAsync(default);
        Assert.False(generation.ClinicConfigured);
    }
}

public class DashboardPageTests
{
    private static readonly Clinic Glow = SomeClinic();

    // ---- dashboard home: date ranges --------------------------------------------------------------------------

    private async Task<(DashboardIndex Model, DateTimeOffset? From, DateTimeOffset? To)> RunAsync(string period, DateOnly? from = null, DateOnly? to = null)
    {
        DateTimeOffset? seenFrom = null, seenTo = null;
        var dashboard = new Mock<IDashboardService>();
        dashboard.Setup(d => d.GetSummaryAsync(Glow.Id, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTimeOffset?, DateTimeOffset?, CancellationToken>((_, f, t, _) => { seenFrom = f; seenTo = t; })
            .ReturnsAsync(Blank<DashboardSummaryResponse>());
        dashboard.Setup(d => d.GetProcedureStatsAsync(Glow.Id, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardProcedureRow>());
        dashboard.Setup(d => d.GetAttentionAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<DashboardAttentionResponse>());
        var health = new Mock<IWhatsAppHealthService>();
        var model = new DashboardIndex(ClinicContext(Glow).Object, dashboard.Object, health.Object) { Period = period, From = from, To = to }.Attach();
        await model.OnGetAsync(default);
        return (model, seenFrom, seenTo);
    }

    [Fact]
    public async Task All_time_sends_no_bounds_and_labels_it()
    {
        var (model, from, to) = await RunAsync("all");
        Assert.Equal(("All time", null, null), (model.RangeLabel, from, to));
        Assert.NotNull(model.Summary);
        Assert.NotNull(model.Attention);
    }

    [Fact]
    public async Task A_custom_range_is_sent_as_midnights_with_an_exclusive_end()
    {
        var (model, from, to) = await RunAsync("custom", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10));
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), to);
        Assert.Equal("Mar 1 – Mar 10, 2026", model.RangeLabel);
    }

    [Fact]
    public async Task A_reversed_custom_range_is_swapped_and_a_single_day_has_a_single_date_label()
    {
        var (swapped, from, to) = await RunAsync("custom", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1));
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), to);
        Assert.NotEqual("All time", swapped.RangeLabel);

        var (single, _, _) = await RunAsync("custom", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 5));
        Assert.Equal("Mar 5, 2026", single.RangeLabel);

        var (openEnded, openFrom, openTo) = await RunAsync("custom", null, null);
        Assert.Equal(("All time", null, null), (openEnded.RangeLabel, openFrom, openTo));
    }

    [Theory]
    [InlineData("this_week")]
    [InlineData("last_week")]
    [InlineData("this_month")]
    [InlineData("last_month")]
    [InlineData("7d")]
    [InlineData("30d")]
    public async Task Preset_periods_resolve_to_a_bounded_range(string period)
    {
        var (model, from, to) = await RunAsync(period);
        Assert.NotNull(from);
        Assert.True(to > from);
        Assert.Equal(period, model.Period);
    }

    [Fact]
    public async Task An_unknown_period_falls_back_to_this_month()
    {
        var (model, from, _) = await RunAsync("bogus");
        Assert.Equal("this_month", model.Period);
        Assert.Equal(1, from!.Value.Day);
    }

    [Fact]
    public async Task Last_30_days_is_30_days_wide_and_this_week_starts_on_a_monday()
    {
        var (_, from30, to30) = await RunAsync("30d");
        Assert.Equal(30, (to30!.Value - from30!.Value).TotalDays);
        var (_, fromWeek, _) = await RunAsync("this_week");
        Assert.Equal(DayOfWeek.Monday, fromWeek!.Value.DayOfWeek);
    }

    [Fact]
    public async Task The_viewer_time_zone_cookie_moves_the_midnights()
    {
        var dashboard = new Mock<IDashboardService>();
        DateTimeOffset? seenFrom = null;
        dashboard.Setup(d => d.GetSummaryAsync(Glow.Id, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTimeOffset?, DateTimeOffset?, CancellationToken>((_, f, _, _) => seenFrom = f)
            .ReturnsAsync(Blank<DashboardSummaryResponse>());
        var model = new DashboardIndex(ClinicContext(Glow).Object, dashboard.Object, Mock.Of<IWhatsAppHealthService>())
        {
            Period = "custom", From = new DateOnly(2026, 3, 1), To = new DateOnly(2026, 3, 2),
        }.Attach(configure: http => http.Request.Headers.Cookie = "sf-tz=Asia/Tokyo");
        await model.OnGetAsync(default);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 15, 0, 0, TimeSpan.Zero), seenFrom);     // Tokyo midnight = 15:00 UTC the day before
    }

    [Fact]
    public async Task Without_a_clinic_the_dashboard_stays_empty()
    {
        var model = new DashboardIndex(ClinicContext(null).Object, Mock.Of<IDashboardService>(), Mock.Of<IWhatsAppHealthService>()).Attach();
        await model.OnGetAsync(default);
        Assert.False(model.ClinicConfigured);
        Assert.Null(model.Summary);
    }

    // ---- leads list ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Leads_page_normalises_the_page_number_and_computes_paging()
    {
        var dashboard = new Mock<IDashboardService>();
        var leads = new Mock<ILeadService>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.DashboardLeadsPageSize).Returns(25);
        dashboard.Setup(d => d.GetLeadsAsync(Glow.Id, "new", "ann", "web", 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DashboardLeadRow>() as IReadOnlyList<DashboardLeadRow>, 60)).Verifiable();
        leads.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "web" });

        var model = new PlasticSurgery.Pages.Dashboard.LeadsModel(ClinicContext(Glow).Object, dashboard.Object, leads.Object, config.Object)
        {
            Status = "new", Search = "ann", Source = "web", PageNumber = -3,
        }.Attach();
        await model.OnGetAsync(default);
        Assert.Equal((1, 60, 3, 25), (model.PageNumber, model.TotalCount, model.TotalPages, model.PageSize));
        Assert.Equal(["web"], model.AllSources);
        dashboard.Verify();

        var none = new PlasticSurgery.Pages.Dashboard.LeadsModel(ClinicContext(null).Object, dashboard.Object, leads.Object, config.Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public async Task Leads_page_two_skips_one_page()
    {
        var dashboard = new Mock<IDashboardService>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.DashboardLeadsPageSize).Returns(10);
        dashboard.Setup(d => d.GetLeadsAsync(Glow.Id, null, null, null, 10, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DashboardLeadRow>() as IReadOnlyList<DashboardLeadRow>, 15)).Verifiable();
        var leads = Mock.Of<ILeadService>(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        var model = new PlasticSurgery.Pages.Dashboard.LeadsModel(ClinicContext(Glow).Object, dashboard.Object, leads, config.Object) { PageNumber = 2 }.Attach();
        await model.OnGetAsync(default);
        dashboard.Verify();
    }

    // ---- lead detail ----------------------------------------------------------------------------------------------

    private static LeadResponse Lead(string status = "new", string? source = "web") =>
        Blank<LeadResponse>() with { Status = status, QualificationStatus = "hot", Source = source };

    [Fact]
    public async Task Lead_detail_404s_for_an_unknown_lead_and_is_empty_without_a_clinic()
    {
        var leads = new Mock<ILeadService>();
        var model = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(Glow).Object, leads.Object) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await model.OnGetAsync(default));

        var none = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(null).Object, leads.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
        Assert.False(none.ClinicConfigured);
    }

    [Theory]
    [InlineData("WEB", "WEB", null)]          // a known source (case-insensitive) is selected as is
    [InlineData("tiktok", "tiktok", null)]    // the lead's own source is always added to the options, so it never lands in "other"
    [InlineData(null, "other", null)]
    public async Task Lead_detail_preselects_status_and_source(string? source, string choice, string? custom)
    {
        var id = Guid.NewGuid();
        var leads = new Mock<ILeadService>();
        leads.Setup(l => l.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Lead("contacted", source));
        leads.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "WEB" });
        var model = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(Glow).Object, leads.Object) { Id = id }.Attach();
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal(("contacted", "hot", choice), (model.Status, model.QualificationStatus, model.SourceChoice));
        Assert.Equal(custom, model.CustomSource);
        Assert.Contains("facebook", model.SourceOptions);                     // suggested sources are always offered
        Assert.Equal(model.SourceOptions.Order(StringComparer.OrdinalIgnoreCase), model.SourceOptions);
    }

    [Fact]
    public async Task Lead_detail_keeps_an_unusual_current_source_in_the_options()
    {
        var id = Guid.NewGuid();
        var leads = new Mock<ILeadService>();
        leads.Setup(l => l.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Lead(source: "tiktok"));
        leads.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        var model = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(Glow).Object, leads.Object) { Id = id }.Attach();
        await model.OnGetAsync(default);
        Assert.Contains("tiktok", model.SourceOptions);
    }

    [Fact]
    public async Task Saving_a_lead_trims_a_custom_source_and_redirects()
    {
        var id = Guid.NewGuid();
        UpdateLeadStatusRequest? seen = null;
        var leads = new Mock<ILeadService>();
        leads.Setup(l => l.UpdateStatusAsync(Glow.Id, id, It.IsAny<UpdateLeadStatusRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, UpdateLeadStatusRequest, CancellationToken>((_, _, r, _) => seen = r)
            .ReturnsAsync(Lead());
        var model = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(Glow).Object, leads.Object)
        {
            Id = id, Status = "qualified", QualificationStatus = "warm", SourceChoice = "other", CustomSource = "  podcast ",
        }.Attach();
        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync(default));
        Assert.Equal(new UpdateLeadStatusRequest("qualified", "warm", "podcast"), seen);

        model.SourceChoice = "web";
        await model.OnPostAsync(default);
        Assert.Equal("web", seen!.Source);
    }

    [Fact]
    public async Task Saving_a_lead_handles_missing_leads_validation_errors_and_missing_clinics()
    {
        var id = Guid.NewGuid();
        var leads = new Mock<ILeadService>();
        leads.Setup(l => l.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Lead());
        leads.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        var model = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(Glow).Object, leads.Object) { Id = id, SourceChoice = "web" }.Attach();

        Assert.IsType<NotFoundResult>(await model.OnPostAsync(default));          // the service returns null

        leads.Setup(l => l.UpdateStatusAsync(Glow.Id, id, It.IsAny<UpdateLeadStatusRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid status."));
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Invalid status.", model.ErrorMessage);
        Assert.NotNull(model.Lead);                                                // the page reloads its data to redisplay

        var none = new PlasticSurgery.Pages.Dashboard.LeadDetailModel(ClinicContext(null).Object, leads.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostAsync(default));
    }

    // ---- appointment detail ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Appointment_detail_shows_status_and_clinic_time_zone()
    {
        var id = Guid.NewGuid();
        var appointments = new Mock<IAppointmentService>();
        appointments.Setup(a => a.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<AppointmentResponse>() with { Status = AppointmentStatus.Confirmed });
        var model = new PlasticSurgery.Pages.Dashboard.AppointmentDetailModel(ClinicContext(SomeClinic(timezone: "Asia/Beirut")).Object, appointments.Object) { Id = id }.Attach();
        // the mock is keyed to Glow; use a clinic context returning the same clinic instead
        var clinic = SomeClinic(timezone: "Asia/Beirut");
        appointments.Setup(a => a.GetByIdAsync(clinic.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<AppointmentResponse>() with { Status = AppointmentStatus.Confirmed });
        model = new PlasticSurgery.Pages.Dashboard.AppointmentDetailModel(ClinicContext(clinic).Object, appointments.Object) { Id = id }.Attach();
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal((AppointmentStatus.Confirmed, "Asia/Beirut"), (model.Status, model.ClinicTimezone));

        var missing = new PlasticSurgery.Pages.Dashboard.AppointmentDetailModel(ClinicContext(clinic).Object, appointments.Object) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await missing.OnGetAsync(default));
        var none = new PlasticSurgery.Pages.Dashboard.AppointmentDetailModel(ClinicContext(null).Object, appointments.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
    }

    [Fact]
    public async Task Saving_an_appointment_status_redirects_404s_or_shows_the_validation_error()
    {
        var id = Guid.NewGuid();
        var clinic = SomeClinic();
        var appointments = new Mock<IAppointmentService>();
        var model = new PlasticSurgery.Pages.Dashboard.AppointmentDetailModel(ClinicContext(clinic).Object, appointments.Object) { Id = id, Status = "attended" }.Attach();

        Assert.IsType<NotFoundResult>(await model.OnPostAsync(default));
        appointments.Setup(a => a.UpdateStatusAsync(clinic.Id, id, "attended", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<AppointmentResponse>());
        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync(default));

        appointments.Setup(a => a.UpdateStatusAsync(clinic.Id, id, "attended", It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Bad status."));
        appointments.Setup(a => a.GetByIdAsync(clinic.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<AppointmentResponse>());
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Bad status.", model.ErrorMessage);

        var none = new PlasticSurgery.Pages.Dashboard.AppointmentDetailModel(ClinicContext(null).Object, appointments.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostAsync(default));
    }
}

public class ProcedurePageTests
{
    private static readonly Clinic Glow = SomeClinic();

    [Fact]
    public async Task Procedure_list_loads_and_toggling_reports_the_result()
    {
        var procedures = new Mock<IProcedureService>();
        procedures.Setup(p => p.ListAsync(Glow.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProcedureResponse>());
        var id = Guid.NewGuid();
        procedures.SetupSequence(p => p.SetActiveAsync(Glow.Id, id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProcedureResponse?)null)
            .ReturnsAsync(Blank<ProcedureResponse>() with { Name = "Botox" })
            .ReturnsAsync(Blank<ProcedureResponse>() with { Name = "Botox" });
        var model = new ProcedureIndex(ClinicContext(Glow).Object, procedures.Object).Attach();

        await model.OnGetAsync(default);
        Assert.True(model.ClinicConfigured);

        await model.OnPostToggleAsync(id, true, default);
        Assert.Equal("Procedure not found.", model.ErrorMessage);
        await model.OnPostToggleAsync(id, false, default);
        Assert.Contains("Botox deactivated", model.StatusMessage);
        await model.OnPostToggleAsync(id, true, default);
        Assert.Equal("Botox is active again.", model.StatusMessage);

        var none = new ProcedureIndex(ClinicContext(null).Object, procedures.Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClinicConfigured);
        Assert.IsType<RedirectToPageResult>(await none.OnPostToggleAsync(id, true, default));
    }

    [Fact]
    public async Task Procedure_edit_loads_an_existing_procedure_or_starts_blank()
    {
        var id = Guid.NewGuid();
        var procedures = new Mock<IProcedureService>();
        procedures.Setup(p => p.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<ProcedureResponse>() with { Name = "Facelift", Code = "FL", ConsultationDuration = 45, Description = "d" });
        var edit = new ProcedureEdit(ClinicContext(Glow).Object, procedures.Object) { Id = id }.Attach();
        Assert.IsType<PageResult>(await edit.OnGetAsync(default));
        Assert.Equal(("Facelift", "FL", 45, "d", false), (edit.Name, edit.Code, edit.DurationMinutes, edit.Description, edit.IsNew));

        var fresh = new ProcedureEdit(ClinicContext(Glow).Object, procedures.Object).Attach();
        Assert.IsType<PageResult>(await fresh.OnGetAsync(default));
        Assert.True(fresh.IsNew);

        var missing = new ProcedureEdit(ClinicContext(Glow).Object, procedures.Object) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await missing.OnGetAsync(default));
        var none = new ProcedureEdit(ClinicContext(null).Object, procedures.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public async Task Procedure_edit_creates_updates_and_reports_problems()
    {
        var id = Guid.NewGuid();
        var procedures = new Mock<IProcedureService>();
        var create = new ProcedureEdit(ClinicContext(Glow).Object, procedures.Object) { Name = "Botox", Code = "B", Description = "d", DurationMinutes = 20 }.Attach();
        Assert.Equal("page:/Procedures/Index", RedirectTarget(await create.OnPostAsync(default)));
        procedures.Verify(p => p.CreateAsync(new CreateProcedureRequest(Glow.Id, "Botox", "B", "d", 20), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Saved.", create.TempData["StatusMessage"]);

        var update = new ProcedureEdit(ClinicContext(Glow).Object, procedures.Object) { Id = id, Name = "Botox" }.Attach();
        Assert.IsType<NotFoundResult>(await update.OnPostAsync(default));          // UpdateAsync returns null

        procedures.Setup(p => p.UpdateAsync(Glow.Id, id, It.IsAny<UpdateProcedureRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ProcedureResponse>());
        Assert.Equal("page:/Procedures/Index", RedirectTarget(await update.OnPostAsync(default)));

        procedures.Setup(p => p.UpdateAsync(Glow.Id, id, It.IsAny<UpdateProcedureRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Name taken."));
        Assert.IsType<PageResult>(await update.OnPostAsync(default));
        Assert.Equal("Name taken.", update.ErrorMessage);

        var none = new ProcedureEdit(ClinicContext(null).Object, procedures.Object).Attach();
        Assert.Equal("page:/Procedures/Index", RedirectTarget(await none.OnPostAsync(default)));
    }
}

public class CampaignDetailsPageTests
{
    private static readonly Clinic Glow = SomeClinic();

    [Fact]
    public async Task Campaign_details_load_404_or_show_empty_without_a_clinic()
    {
        var id = Guid.NewGuid();
        var campaigns = new Mock<ICampaignService>();
        var model = new CampaignDetails(ClinicContext(Glow).Object, campaigns.Object).Attach();
        Assert.IsType<NotFoundResult>(await model.OnGetAsync(id, default));

        campaigns.Setup(c => c.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<CampaignDetailsResponse>());
        Assert.IsType<PageResult>(await model.OnGetAsync(id, default));
        Assert.NotNull(model.Details);

        var none = new CampaignDetails(ClinicContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(id, default));
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public async Task Sending_reports_progress_completion_or_the_error()
    {
        var id = Guid.NewGuid();
        var campaigns = new Mock<ICampaignService>();
        campaigns.SetupSequence(c => c.SendAsync(Glow.Id, id, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<ProcessCampaignBatchResult>() with { Processed = 20, Succeeded = 18, Failed = 2, RemainingQueued = 5, CampaignCompleted = false })
            .ReturnsAsync(Blank<ProcessCampaignBatchResult>() with { Processed = 5, Succeeded = 5, Failed = 0, RemainingQueued = 0, CampaignCompleted = true })
            .ReturnsAsync((ProcessCampaignBatchResult?)null)
            .ThrowsAsync(new InvalidOperationException("Not approved yet."));
        var model = new CampaignDetails(ClinicContext(Glow).Object, campaigns.Object).Attach();

        Assert.IsType<RedirectToPageResult>(await model.OnPostSendAsync(id, default));
        Assert.Contains("Processed 20 (18 sent, 2 failed). 5 still waiting", model.StatusMessage);
        await model.OnPostSendAsync(id, default);
        Assert.Contains("Campaign completed.", model.StatusMessage);
        await model.OnPostSendAsync(id, default);
        Assert.Null(model.StatusMessage);
        await model.OnPostSendAsync(id, default);
        Assert.Equal("Not approved yet.", model.ErrorMessage);

        var none = new CampaignDetails(ClinicContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostSendAsync(id, default));
    }

    [Fact]
    public async Task Cancelling_reports_success_or_the_error()
    {
        var id = Guid.NewGuid();
        var campaigns = new Mock<ICampaignService>();
        var model = new CampaignDetails(ClinicContext(Glow).Object, campaigns.Object).Attach();
        await model.OnPostCancelAsync(id, default);
        Assert.Equal("Campaign cancelled.", model.StatusMessage);

        campaigns.Setup(c => c.CancelAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Already sent."));
        await model.OnPostCancelAsync(id, default);
        Assert.Equal("Already sent.", model.ErrorMessage);

        var none = new CampaignDetails(ClinicContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostCancelAsync(id, default));
    }
}
