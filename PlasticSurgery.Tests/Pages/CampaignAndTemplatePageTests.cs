using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Entities.Responses.Procedures;using PlasticSurgery.Tests.Controllers;
using static PlasticSurgery.Tests.Pages.PageKit;
using CampaignCreate = PlasticSurgery.Pages.Campaigns.CreateModel;
using TemplateIndex = PlasticSurgery.Pages.WhatsApp.Templates.IndexModel;

namespace PlasticSurgery.Tests.Pages;

public class CampaignCreatePageTests
{
    private static readonly Clinic Glow = SomeClinic(timezone: "UTC");
    private readonly Mock<IWhatsAppTemplateService> _templates = new();
    private readonly Mock<ILeadService> _leads = new();
    private readonly Mock<IProcedureService> _procedures = new();
    private readonly Mock<ICampaignService> _campaigns = new();
    private CreateCampaignRequest? _created;

    public CampaignCreatePageTests()
    {
        _leads.Setup(l => l.ListAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<LeadResponse>() as IReadOnlyList<LeadResponse>, 0));
        _leads.Setup(l => l.GetDistinctSourcesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "web" });
        _procedures.Setup(p => p.ListAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProcedureResponse>());
        _templates.Setup(t => t.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppTemplateResponse>());
        _campaigns.Setup(c => c.CreateAsync(It.IsAny<CreateCampaignRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateCampaignRequest, CancellationToken>((r, _) => _created = r)
            .ReturnsAsync(Blank<CampaignResponse>() with { Id = Guid.NewGuid() });
    }

    private CampaignCreate Sut(Clinic? clinic = null, bool none = false, Dictionary<string, string?>? config = null)
    {
        var configuration = ControllerTestKit.Config((config ?? new()).Select(kv => (kv.Key, kv.Value)).ToArray());
        return new CampaignCreate(ClinicContext(none ? null : clinic ?? Glow).Object, _templates.Object, _leads.Object, _procedures.Object, _campaigns.Object, configuration).Attach();
    }

    private static WhatsAppTemplateResponse Template(Guid id, string status = WhatsAppTemplateStatus.Approved, string? provider = null, string body = "Hello {{1}}, see you {{2}} {{1}}") =>
        Blank<WhatsAppTemplateResponse>() with { Id = id, Status = status, Provider = provider, Body = body };

    private static LeadResponse Lead(Guid id, string? full, string? first, string? phone) =>
        Blank<LeadResponse>() with { Id = id, FullName = full, FirstName = first, Phone = phone };

    // ---- load -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_approved_templates_of_the_active_provider_are_offered_and_placeholders_are_found()
    {
        var approved = Guid.NewGuid();
        _templates.Setup(t => t.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppTemplateResponse>
        {
            Template(approved), Template(Guid.NewGuid(), WhatsAppTemplateStatus.Pending),
            Template(Guid.NewGuid(), provider: "infobip"),
        });
        var model = Sut(); model.WhatsAppTemplateId = approved;
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal([approved], model.ApprovedTemplates.Select(t => t.Id));
        Assert.Equal([1, 2], model.PlaceholderNumbers);
        Assert.NotNull(model.SelectedTemplate);
        Assert.Equal(["web"], model.LeadSources);
        Assert.Equal("UTC", model.ViewerTimezone);
    }

    [Fact]
    public async Task Without_a_clinic_the_page_loads_empty()
    {
        var model = Sut(none: true);
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.False(model.ClinicConfigured);
        Assert.Empty(model.ApprovedTemplates);
    }

    [Theory]
    [InlineData("custom", true, "manual")]
    [InlineData("custom", false, "custom")]
    [InlineData("all_eligible", false, "all_eligible")]
    [InlineData("reactivation_no_consultation", false, "reactivation_no_consultation")]
    public void The_audience_choice_summarises_type_and_manual_selection(string type, bool manual, string expected)
    {
        var model = Sut(); model.AudienceType = type; model.ManualSelection = manual;
        Assert.Equal(expected, model.AudienceChoice);
    }

    // ---- validation -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_campaign_needs_a_name_and_a_template()
    {
        var model = Sut();
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Give the campaign a name and choose a template.", model.ErrorMessage);
        model.Name = "Spring"; model.WhatsAppTemplateId = null;
        await model.OnPostAsync(default);
        Assert.Equal("Give the campaign a name and choose a template.", model.ErrorMessage);
        _campaigns.Verify(c => c.CreateAsync(It.IsAny<CreateCampaignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsType<RedirectToPageResult>(await Sut(none: true).OnPostAsync(default));
    }

    [Fact]
    public async Task Manual_selection_needs_at_least_one_person_and_blanks_need_manual_selection()
    {
        var model = Sut(); model.Name = "n"; model.WhatsAppTemplateId = Guid.NewGuid(); model.AudienceType = CampaignAudienceType.Custom; model.ManualSelection = true;
        await model.OnPostAsync(default);
        Assert.StartsWith("Tick at least one person", model.ErrorMessage);

        var blanks = Sut(); blanks.Name = "n"; blanks.WhatsAppTemplateId = Guid.NewGuid(); blanks.Variables = new() { [1] = "Hi" };
        await blanks.OnPostAsync(default);
        Assert.StartsWith("This template has blanks to fill", blanks.ErrorMessage);
    }

    [Fact]
    public async Task Scheduling_needs_a_date_and_time_in_the_future()
    {
        var model = Sut(); model.Name = "n"; model.WhatsAppTemplateId = Guid.NewGuid(); model.SendOption = "later";
        await model.OnPostAsync(default);
        Assert.StartsWith("Pick the date and time to send", model.ErrorMessage);

        model.ScheduleDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)); model.ScheduleTime = new TimeOnly(9, 0);
        await model.OnPostAsync(default);
        Assert.StartsWith("That date and time has already passed", model.ErrorMessage);
        _campaigns.Verify(c => c.CreateAsync(It.IsAny<CreateCampaignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- creating ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sending_now_to_a_reactivation_audience_builds_filters_and_sends_the_first_batch()
    {
        var model = Sut(); model.Name = "Win back"; model.WhatsAppTemplateId = Guid.NewGuid(); model.SendOption = "now";
        model.InactiveDays = 45; model.ReactivationSource = "web";
        var result = await model.OnPostAsync(default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(CampaignType.Reactivation, _created!.CampaignType);
        Assert.Empty(_created.LeadIds);
        Assert.Null(_created.VariablesByLeadId);
        var filters = CampaignAudienceFilters.Parse(_created.AudienceFilters);
        Assert.Equal((45, "web"), (filters.InactiveDays, Assert.Single(filters.Sources!)));
        _campaigns.Verify(c => c.SendAsync(Glow.Id, It.IsAny<Guid>(), 20, It.IsAny<CancellationToken>()), Times.Once);
        _campaigns.Verify(c => c.ScheduleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task All_eligible_sends_no_filters_and_no_campaign_type()
    {
        var model = Sut(); model.Name = "Everyone"; model.WhatsAppTemplateId = Guid.NewGuid(); model.AudienceType = CampaignAudienceType.AllEligible; model.SendOption = "draft";
        await model.OnPostAsync(default);
        Assert.Null(_created!.AudienceFilters);
        Assert.Null(_created.CampaignType);
        _campaigns.Verify(c => c.SendAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);   // "draft": neither sent nor scheduled
        _campaigns.Verify(c => c.ScheduleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Custom_filters_become_utc_midnights_with_an_inclusive_end_date()
    {
        var model = Sut(); model.Name = "Custom"; model.WhatsAppTemplateId = Guid.NewGuid(); model.AudienceType = CampaignAudienceType.Custom; model.SendOption = "draft";
        model.CustomInactiveDays = 10; model.CustomLeadStatuses = ["new"]; model.CustomSources = ["web"]; model.CustomQualificationStatuses = ["hot"];
        model.CustomAppointmentStatuses = ["attended"]; model.CustomCountry = "LB"; model.CustomCity = "Beirut";
        model.CustomCreatedAfter = new DateTime(2026, 3, 1); model.CustomCreatedBefore = new DateTime(2026, 3, 10);
        model.CustomLastContactedAfter = new DateTime(2026, 2, 1); model.CustomLastContactedBefore = new DateTime(2026, 2, 5);
        await model.OnPostAsync(default);

        var f = CampaignAudienceFilters.Parse(_created!.AudienceFilters);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), f.CreatedAfter);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), f.CreatedBefore);      // "before the 10th" includes the 10th
        Assert.Equal(new DateTimeOffset(2026, 2, 6, 0, 0, 0, TimeSpan.Zero), f.LastContactedBefore);
        Assert.Equal((10, "LB", "Beirut"), (f.InactiveDays, Assert.Single(f.Countries!), Assert.Single(f.Cities!)));
        Assert.Equal(["new"], f.LeadStatuses);
        Assert.Equal(["attended"], f.AppointmentStatuses);
        Assert.Equal(["hot"], f.QualificationStatuses);
    }

    [Fact]
    public async Task Manual_selection_resolves_per_lead_variables_in_number_order()
    {
        var ann = Guid.NewGuid(); var bob = Guid.NewGuid(); var ghost = Guid.NewGuid();
        _leads.Setup(l => l.ListAsync(Glow.Id, null, null, null, 0, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<LeadResponse> { Lead(ann, "Ann Smith", "Ann", "+1"), Lead(bob, "Bob Jones", null, "+2") } as IReadOnlyList<LeadResponse>, 2));
        var model = Sut(); model.Name = "Manual"; model.WhatsAppTemplateId = Guid.NewGuid(); model.AudienceType = CampaignAudienceType.Custom; model.ManualSelection = true;
        model.SendOption = "draft"; model.LeadIds = [ann, bob, ghost];
        model.Variables = new() { [2] = "{LeadPhone}", [1] = "{LeadFirstName}" };
        await model.OnPostAsync(default);

        Assert.Null(_created!.AudienceFilters);
        Assert.Equal([ann, bob, ghost], _created.LeadIds);
        Assert.Equal(["Ann", "+1"], _created.VariablesByLeadId![ann]);
        Assert.Equal(["Bob Jones", "+2"], _created.VariablesByLeadId[bob]);       // no first name: falls back to the full name
        Assert.DoesNotContain(ghost, _created.VariablesByLeadId.Keys);            // a lead that no longer exists is skipped
    }

    [Fact]
    public async Task Literal_and_full_name_variables_pass_through()
    {
        var ann = Guid.NewGuid();
        _leads.Setup(l => l.ListAsync(Glow.Id, null, null, null, 0, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<LeadResponse> { Lead(ann, null, null, null) } as IReadOnlyList<LeadResponse>, 1));
        var model = Sut(); model.Name = "m"; model.WhatsAppTemplateId = Guid.NewGuid(); model.AudienceType = CampaignAudienceType.Custom; model.ManualSelection = true;
        model.SendOption = "draft"; model.LeadIds = [ann]; model.Variables = new() { [1] = "{LeadFullName}", [2] = "Spring sale", [3] = "{LeadPhone}", [4] = "{LeadFirstName}" };
        await model.OnPostAsync(default);
        Assert.Equal(["", "Spring sale", "", ""], _created!.VariablesByLeadId![ann]);
    }

    [Fact]
    public async Task Scheduling_for_later_converts_the_viewer_time_and_schedules_the_campaign()
    {
        var model = Sut(); model.Name = "Later"; model.WhatsAppTemplateId = Guid.NewGuid(); model.SendOption = "later";
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        model.ScheduleDate = day; model.ScheduleTime = new TimeOnly(10, 30);
        await model.OnPostAsync(default);

        var expected = new DateTimeOffset(day.Year, day.Month, day.Day, 10, 30, 0, TimeSpan.Zero);
        Assert.Equal(expected, _created!.ScheduledAt);
        _campaigns.Verify(c => c.ScheduleAsync(Glow.Id, It.IsAny<Guid>(), expected, It.IsAny<CancellationToken>()), Times.Once);
        _campaigns.Verify(c => c.SendAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Service_rejections_are_shown_and_the_page_reloads()
    {
        _campaigns.Setup(c => c.CreateAsync(It.IsAny<CreateCampaignRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Template not approved."));
        var model = Sut(); model.Name = "n"; model.WhatsAppTemplateId = Guid.NewGuid();
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Template not approved.", model.ErrorMessage);
        Assert.True(model.ClinicConfigured);
    }
}

public class WhatsAppTemplatePageTests
{
    private static readonly Clinic Glow = SomeClinic();
    private readonly Mock<IWhatsAppTemplateService> _templates = new();
    private TemplateIndex Sut(bool none = false) => new TemplateIndex(ClinicContext(none ? null : Glow).Object, _templates.Object).Attach();

    private static WhatsAppTemplateResponse Template(string status, string name = "promo", string? rejection = null) =>
        Blank<WhatsAppTemplateResponse>() with { Name = name, Status = status, RejectionReason = rejection };

    [Fact]
    public async Task The_page_lists_templates_or_is_empty_without_a_clinic()
    {
        _templates.Setup(t => t.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppTemplateResponse> { Template("approved") });
        var model = Sut();
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal((true, Glow.Id, 1), (model.ClinicConfigured, model.ClinicId, model.Templates.Count));
        var none = Sut(true);
        await none.OnGetAsync(default);
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public async Task Creating_a_template_reports_the_outcome_and_drops_an_empty_header_type()
    {
        _templates.SetupSequence(t => t.CreateAsync(It.IsAny<CreateWhatsAppTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Template(WhatsAppTemplateStatus.Pending))
            .ReturnsAsync(Template(WhatsAppTemplateStatus.Rejected, rejection: "bad format"))
            .ThrowsAsync(new ArgumentException("Name is required."));
        var model = Sut();
        model.Name = "promo"; model.Body = "hi"; model.HeaderType = WhatsAppTemplateHeaderType.None;

        Assert.IsType<RedirectToPageResult>(await model.OnPostCreateAsync(default));
        Assert.Equal("Template 'promo' saved — status: pending.", model.StatusMessage);
        Assert.Null(model.ErrorMessage);

        await model.OnPostCreateAsync(default);
        Assert.Null(model.StatusMessage);
        Assert.Contains("submission failed: bad format", model.ErrorMessage);

        await model.OnPostCreateAsync(default);
        Assert.Equal("Name is required.", model.ErrorMessage);

        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostCreateAsync(default));
    }

    [Fact]
    public async Task A_header_type_other_than_none_is_forwarded()
    {
        CreateWhatsAppTemplateRequest? seen = null;
        _templates.Setup(t => t.CreateAsync(It.IsAny<CreateWhatsAppTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateWhatsAppTemplateRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Template("approved"));
        var model = Sut();
        model.HeaderType = WhatsAppTemplateHeaderType.All.First(h => h != WhatsAppTemplateHeaderType.None); model.HeaderContent = "Hello";
        await model.OnPostCreateAsync(default);
        Assert.Equal(model.HeaderType, seen!.HeaderType);
        Assert.Equal("Hello", seen.HeaderContent);
    }

    [Fact]
    public async Task Syncing_reports_the_new_status_or_the_provider_error()
    {
        var id = Guid.NewGuid();
        _templates.SetupSequence(t => t.SyncStatusAsync(Glow.Id, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Template("approved")).ReturnsAsync((WhatsAppTemplateResponse?)null)
            .ThrowsAsync(new InvalidOperationException("Not connected.")).ThrowsAsync(new MetaGraphApiException("Meta is down."));
        var model = Sut();
        await model.OnPostSyncAsync(id, default);
        Assert.Equal("'promo' status refreshed: approved.", model.StatusMessage);
        await model.OnPostSyncAsync(id, default);
        Assert.Null(model.StatusMessage);
        await model.OnPostSyncAsync(id, default);
        Assert.Equal("Not connected.", model.ErrorMessage);
        await model.OnPostSyncAsync(id, default);
        Assert.Equal("Meta is down.", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostSyncAsync(id, default));
    }

    [Fact]
    public async Task Retrying_reports_acceptance_rejection_or_the_error()
    {
        var id = Guid.NewGuid();
        _templates.SetupSequence(t => t.RetrySubmitAsync(Glow.Id, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Template("pending")).ReturnsAsync(Template(WhatsAppTemplateStatus.Rejected, rejection: "still bad"))
            .ReturnsAsync((WhatsAppTemplateResponse?)null).ThrowsAsync(new InvalidOperationException("Not a draft."));
        var model = Sut();
        await model.OnPostRetryAsync(id, default);
        Assert.Contains("was sent to Meta for review", model.StatusMessage);
        await model.OnPostRetryAsync(id, default);
        Assert.Contains("Meta still didn't accept 'promo': still bad", model.ErrorMessage);
        await model.OnPostRetryAsync(id, default);
        await model.OnPostRetryAsync(id, default);
        Assert.Equal("Not a draft.", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostRetryAsync(id, default));
    }
}
