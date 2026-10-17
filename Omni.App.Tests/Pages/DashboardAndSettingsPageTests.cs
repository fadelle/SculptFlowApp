using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Entities.Responses.Catalog;
using static Omni.App.Tests.Pages.PageKit;
using DashboardIndex = Omni.App.Pages.Dashboard.IndexModel;
using ServiceIndex = Omni.App.Pages.Services.IndexModel;
using ServiceEdit = Omni.App.Pages.Services.EditModel;
using MembersIndex = Omni.App.Pages.Members.IndexModel;
using InboxIndex = Omni.App.Pages.Inbox.IndexModel;
using CampaignIndex = Omni.App.Pages.Campaigns.IndexModel;
using CampaignDetails = Omni.App.Pages.Campaigns.DetailsModel;
using HealthIndex = Omni.App.Pages.WhatsApp.Health.IndexModel;

namespace Omni.App.Tests.Pages;

/// <summary>"No client yet" must be a harmless empty page for every list page, never an exception.</summary>
public class SimpleListPageTests
{
    private static readonly Client Glow = SomeClient();

    private static Mock<IEntitlementService> Seats(int? limit)
    {
        var entitlements = new Mock<IEntitlementService>();
        var values = limit is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [EntitlementKeys.MaxUsers] = limit.Value.ToString() };
        entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(limit is null ? ClientEntitlements.Unrestricted : new ClientEntitlements(true, true, "active", "p", "P", null, values));
        return entitlements;
    }

    private static MemberResponse Person(string id, bool active = true, string role = UserRole.Member) =>
        new(id, "Name " + id, id + "@x.com", role, active, DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task Members_page_lists_members_and_knows_the_current_user()
    {
        var members = new Mock<IMemberService>();
        members.Setup(s => s.ListAsync(Glow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemberResponse> { Person("user-1", role: UserRole.Client), Person("m1"), Person("m2", active: false) });
        var model = new MembersIndex(ClientContext(Glow).Object, members.Object, Seats(5).Object, TextKit.Localizer()).Attach(UserWithId("user-1"));
        await model.OnGetAsync(default);
        Assert.True(model.ClientConfigured);
        Assert.Equal(("Glow Client", "user-1", true), (model.ClientName, model.CurrentUserId, model.CanManage));
        Assert.Equal((3, 2, (int?)5), (model.Members.Count, model.SeatsInUse, model.SeatLimit));

        var empty = new MembersIndex(ClientContext(null).Object, members.Object, Seats(null).Object, TextKit.Localizer()).Attach();
        await empty.OnGetAsync(default);
        Assert.False(empty.ClientConfigured);
        Assert.Empty(empty.Members);
    }

    [Fact]
    public async Task A_member_sees_the_list_but_cannot_manage_it()
    {
        var members = new Mock<IMemberService>();
        members.Setup(s => s.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MemberResponse> { Person("m1") });
        var model = new MembersIndex(ClientContext(Glow, UserRole.Member, "m1").Object, members.Object, Seats(null).Object, TextKit.Localizer()).Attach();
        await model.OnGetAsync(default);
        Assert.Equal((false, (int?)null), (model.CanManage, model.SeatLimit));
    }

    [Fact]
    public async Task Adding_a_member_redirects_with_a_message_and_a_refusal_stays_on_the_page()
    {
        var members = new Mock<IMemberService>();
        members.Setup(s => s.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MemberResponse>());
        members.Setup(s => s.AddAsync(It.Is<UserAccess>(a => a.ClientId == Glow.Id), new AddMemberRequest("Ann Lee", "ann@x.com", "Pw-12345"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Person("ann"));
        var model = new MembersIndex(ClientContext(Glow).Object, members.Object, Seats(null).Object, TextKit.Localizer())
            { FullName = "Ann Lee", Email = "ann@x.com", Password = "Pw-12345" }.Attach();
        Assert.Equal("page:(same)", RedirectTarget(await model.OnPostAddAsync(default)));
        Assert.Equal("Name ann can sign in now.", model.StatusMessage);

        members.Setup(s => s.AddAsync(It.IsAny<UserAccess>(), It.IsAny<AddMemberRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("members.errors.emailTaken"));
        var refused = new MembersIndex(ClientContext(Glow).Object, members.Object, Seats(null).Object, TextKit.Localizer())
            { FullName = "Ann Lee", Email = "ann@x.com", Password = "Pw-12345" }.Attach();
        Assert.IsType<PageResult>(await refused.OnPostAddAsync(default));
        Assert.Equal(("Someone already signs in with this email.", null), (refused.ErrorMessage, refused.Password));
        Assert.Equal("ann@x.com", refused.Email);
    }

    [Fact]
    public async Task Deactivating_goes_through_the_member_rules()
    {
        var members = new Mock<IMemberService>();
        members.Setup(s => s.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MemberResponse>());
        var model = new MembersIndex(ClientContext(Glow).Object, members.Object, Seats(null).Object, TextKit.Localizer()).Attach();
        Assert.Equal("page:(same)", RedirectTarget(await model.OnPostDeactivateAsync("m1", "Mo", default)));
        Assert.Equal("Mo can't sign in any more.", model.StatusMessage);
        members.Verify(s => s.SetActiveAsync(It.Is<UserAccess>(a => a.Role == UserRole.Client), "m1", false, It.IsAny<CancellationToken>()), Times.Once);

        members.Setup(s => s.SetActiveAsync(It.IsAny<UserAccess>(), "m1", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.MaxUsers, "Your plan allows 2 seats."));
        var full = new MembersIndex(ClientContext(Glow).Object, members.Object, Seats(2).Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await full.OnPostReactivateAsync("m1", "Mo", default));
        Assert.Equal("Your plan allows 2 seats.", full.ErrorMessage);

        var nobody = new MembersIndex(ClientContext(null).Object, members.Object, Seats(null).Object, TextKit.Localizer()).Attach();
        Assert.IsType<ForbidResult>(await nobody.OnPostReactivateAsync("m1", "Mo", default));
    }

    [Fact]
    public async Task Inbox_loads_the_first_fifty_conversations()
    {
        var conversations = new Mock<IConversationService>();
        conversations.Setup(c => c.ListAsync(Glow.Id, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ConversationListRow>() as IReadOnlyList<ConversationListRow>, 0)).Verifiable();
        var inboxSettings = new Mock<IInboxSettingsService>();
        inboxSettings.Setup(a => a.GetAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new InboxSettingsDto(false, true, true));
        var model = new InboxIndex(ClientContext(Glow).Object, conversations.Object, inboxSettings.Object, PipelineKit.Types().Object).Attach();
        await model.OnGetAsync(default);
        Assert.Equal((true, Glow.Id, false), (model.ClientConfigured, model.ClientId, model.AiAssistantAvailable));
        Assert.Equal(["New", "Booked", "Attended", "Customer"], InboxIndex.JourneyCategories.Select(model.JourneyLabel)); // the client's own stage labels
        conversations.Verify();

        var none = new InboxIndex(ClientContext(null).Object, conversations.Object, inboxSettings.Object, PipelineKit.Types().Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
    }

    [Fact]
    public async Task Campaign_list_whatsapp_health_and_billing_pages_load_for_a_client_and_skip_without_one()
    {
        var campaigns = new Mock<ICampaignService>();
        campaigns.Setup(c => c.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignListRow>());
        var campaignPage = new CampaignIndex(ClientContext(Glow).Object, campaigns.Object).Attach();
        await campaignPage.OnGetAsync(default);
        Assert.True(campaignPage.ClientConfigured);
        var noCampaigns = new CampaignIndex(ClientContext(null).Object, campaigns.Object).Attach();
        await noCampaigns.OnGetAsync(default);
        Assert.False(noCampaigns.ClientConfigured);

        var health = new Mock<IWhatsAppHealthService>();
        health.Setup(h => h.GetHealthAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WhatsAppHealthResponse>());
        health.Setup(h => h.GetHealthEventsAsync(Glow.Id, 0, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEventResponse>());
        var healthPage = new HealthIndex(ClientContext(Glow).Object, health.Object).Attach();
        await healthPage.OnGetAsync(default);
        Assert.Equal((true, Glow.Id), (healthPage.ClientConfigured, healthPage.ClientId));
        Assert.NotNull(healthPage.Health);
        var noHealth = new HealthIndex(ClientContext(null).Object, health.Object).Attach();
        await noHealth.OnGetAsync(default);
        Assert.False(noHealth.ClientConfigured);

        static Omni.App.Business.Contracts.Managers.IConfigManager BillingOn()
        {
            var config = new Mock<Omni.App.Business.Contracts.Managers.IConfigManager>();
            config.SetupGet(c => c.BillingEnabled).Returns(true);
            return config.Object;
        }

        var billing = new Mock<IBillingQueryService>();
        billing.Setup(b => b.GetSummaryAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ClientBillingSummary>());
        var billingPage = new Omni.App.Pages.Settings.BillingModel(ClientContext(Glow).Object, billing.Object, BillingOn()).Attach();
        await billingPage.OnGetAsync(default);
        Assert.True(billingPage.ClientConfigured);
        Assert.NotNull(billingPage.Summary);
        var noBilling = new Omni.App.Pages.Settings.BillingModel(ClientContext(null).Object, billing.Object, BillingOn()).Attach();
        await noBilling.OnGetAsync(default);
        Assert.False(noBilling.ClientConfigured);
    }

    [Fact]
    public async Task Bookings_page_only_needs_the_client()
    {
        var bookings = new Omni.App.Pages.Dashboard.BookingsModel(ClientContext(SomeClient(timezone: "Asia/Beirut")).Object).Attach();
        await bookings.OnGetAsync(default);
        Assert.Equal(("Asia/Beirut", true), (bookings.ClientTimezone, bookings.ClientConfigured));
        var blankZone = new Omni.App.Pages.Dashboard.BookingsModel(ClientContext(SomeClient(timezone: " ")).Object).Attach();
        await blankZone.OnGetAsync(default);
        Assert.Equal("UTC", blankZone.ClientTimezone);
        var none = new Omni.App.Pages.Dashboard.BookingsModel(ClientContext(null).Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
    }
}

public class DashboardPageTests
{
    private static readonly Client Glow = SomeClient();

    // ---- dashboard home: date ranges --------------------------------------------------------------------------

    private async Task<(DashboardIndex Model, DateTimeOffset? From, DateTimeOffset? To)> RunAsync(string period, DateOnly? from = null, DateOnly? to = null)
    {
        DateTimeOffset? seenFrom = null, seenTo = null;
        var dashboard = new Mock<IDashboardService>();
        dashboard.Setup(d => d.GetSummaryAsync(Glow.Id, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTimeOffset?, DateTimeOffset?, CancellationToken>((_, f, t, _) => { seenFrom = f; seenTo = t; })
            .ReturnsAsync(Blank<DashboardSummaryResponse>());
        dashboard.Setup(d => d.GetServiceStatsAsync(Glow.Id, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardServiceRow>());
        dashboard.Setup(d => d.GetAttentionAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<DashboardAttentionResponse>());
        var health = new Mock<IWhatsAppHealthService>();
        var model = new DashboardIndex(ClientContext(Glow).Object, dashboard.Object, health.Object) { Period = period, From = from, To = to }.Attach();
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
        var model = new DashboardIndex(ClientContext(Glow).Object, dashboard.Object, Mock.Of<IWhatsAppHealthService>())
        {
            Period = "custom", From = new DateOnly(2026, 3, 1), To = new DateOnly(2026, 3, 2),
        }.Attach(configure: http => http.Request.Headers.Cookie = "omni-tz=Asia/Tokyo");
        await model.OnGetAsync(default);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 15, 0, 0, TimeSpan.Zero), seenFrom);     // Tokyo midnight = 15:00 UTC the day before
    }

    [Fact]
    public async Task Without_a_client_the_dashboard_stays_empty()
    {
        var model = new DashboardIndex(ClientContext(null).Object, Mock.Of<IDashboardService>(), Mock.Of<IWhatsAppHealthService>()).Attach();
        await model.OnGetAsync(default);
        Assert.False(model.ClientConfigured);
        Assert.Null(model.Summary);
    }

    // ---- contacts list ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Contacts_page_normalises_the_page_number_and_computes_paging()
    {
        var dashboard = new Mock<IDashboardService>();
        var contacts = new Mock<IContactService>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.DashboardContactsPageSize).Returns(25);
        dashboard.Setup(d => d.GetContactsAsync(Glow.Id, "new", "ann", "web", 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DashboardContactRow>() as IReadOnlyList<DashboardContactRow>, 60)).Verifiable();
        contacts.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "web" });

        var model = new Omni.App.Pages.Dashboard.ContactsModel(ClientContext(Glow).Object, dashboard.Object, contacts.Object, config.Object)
        {
            Stage = "new", Search = "ann", Source = "web", PageNumber = -3,
        }.Attach();
        IReadOnlyList<PipelineStageOption> stages = [new("new", StageCategory.Open, "New")];
        contacts.Setup(l => l.ListStagesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stages);
        await model.OnGetAsync(default);
        Assert.Equal((1, 60, 3, 25), (model.PageNumber, model.TotalCount, model.TotalPages, model.PageSize));
        Assert.Equal(["web"], model.AllSources);
        Assert.Same(stages, model.Stages);
        dashboard.Verify();

        var none = new Omni.App.Pages.Dashboard.ContactsModel(ClientContext(null).Object, dashboard.Object, contacts.Object, config.Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
    }

    [Fact]
    public async Task Contacts_page_two_skips_one_page()
    {
        var dashboard = new Mock<IDashboardService>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.DashboardContactsPageSize).Returns(10);
        dashboard.Setup(d => d.GetContactsAsync(Glow.Id, null, null, null, 10, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DashboardContactRow>() as IReadOnlyList<DashboardContactRow>, 15)).Verifiable();
        var contacts = Mock.Of<IContactService>(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        var model = new Omni.App.Pages.Dashboard.ContactsModel(ClientContext(Glow).Object, dashboard.Object, contacts, config.Object) { PageNumber = 2 }.Attach();
        await model.OnGetAsync(default);
        dashboard.Verify();
    }

    // ---- contact detail ----------------------------------------------------------------------------------------------

    private static ContactResponse Contact(string status = "new", string? source = "web") =>
        Blank<ContactResponse>() with { StageKey = status, Qualification = "hot", Source = source };

    [Fact]
    public async Task Contact_detail_404s_for_an_unknown_contact_and_is_empty_without_a_client()
    {
        var contacts = new Mock<IContactService>();
        var model = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(Glow).Object, contacts.Object, TextKit.Localizer()) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await model.OnGetAsync(default));

        var none = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(null).Object, contacts.Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
        Assert.False(none.ClientConfigured);
    }

    [Theory]
    [InlineData("WEB", "WEB", null)]          // a known source (case-insensitive) is selected as is
    [InlineData("tiktok", "tiktok", null)]    // the contact's own source is always added to the options, so it never lands in "other"
    [InlineData(null, "other", null)]
    public async Task Contact_detail_preselects_status_and_source(string? source, string choice, string? custom)
    {
        var id = Guid.NewGuid();
        var contacts = new Mock<IContactService>();
        contacts.Setup(l => l.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Contact("contacted", source));
        contacts.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "WEB" });
        var model = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(Glow).Object, contacts.Object, TextKit.Localizer()) { Id = id }.Attach();
        IReadOnlyList<PipelineStageOption> stages = [new("contacted", StageCategory.Open, "Reached out")];
        contacts.Setup(l => l.ListStagesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stages);
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Same(stages, model.Stages);
        Assert.Equal(("contacted", "hot", choice), (model.StageKey, model.Qualification, model.SourceChoice));
        Assert.Equal(custom, model.CustomSource);
        Assert.Contains("facebook", model.SourceOptions);                     // suggested sources are always offered
        Assert.Equal(model.SourceOptions.Order(StringComparer.OrdinalIgnoreCase), model.SourceOptions);
    }

    [Fact]
    public async Task Contact_detail_keeps_an_unusual_current_source_in_the_options()
    {
        var id = Guid.NewGuid();
        var contacts = new Mock<IContactService>();
        contacts.Setup(l => l.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Contact(source: "tiktok"));
        contacts.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        var model = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(Glow).Object, contacts.Object, TextKit.Localizer()) { Id = id }.Attach();
        await model.OnGetAsync(default);
        Assert.Contains("tiktok", model.SourceOptions);
    }

    [Fact]
    public async Task Saving_a_contact_trims_a_custom_source_and_redirects()
    {
        var id = Guid.NewGuid();
        UpdateContactStageRequest? seen = null;
        var contacts = new Mock<IContactService>();
        contacts.Setup(l => l.UpdateStageAsync(Glow.Id, id, It.IsAny<UpdateContactStageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, UpdateContactStageRequest, CancellationToken>((_, _, r, _) => seen = r)
            .ReturnsAsync(Contact());
        var model = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(Glow).Object, contacts.Object, TextKit.Localizer())
        {
            Id = id, StageKey = "qualified", Qualification = "warm", SourceChoice = "other", CustomSource = "  podcast ",
        }.Attach();
        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync(default));
        Assert.Equal(new UpdateContactStageRequest("qualified", "warm", "podcast"), seen);

        model.SourceChoice = "web";
        await model.OnPostAsync(default);
        Assert.Equal("web", seen!.Source);
    }

    [Fact]
    public async Task Saving_a_contact_handles_missing_contacts_validation_errors_and_missing_clients()
    {
        var id = Guid.NewGuid();
        var contacts = new Mock<IContactService>();
        contacts.Setup(l => l.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Contact());
        contacts.Setup(l => l.GetDistinctSourcesAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        var model = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(Glow).Object, contacts.Object, TextKit.Localizer()) { Id = id, SourceChoice = "web" }.Attach();

        Assert.IsType<NotFoundResult>(await model.OnPostAsync(default));          // the service returns null

        contacts.Setup(l => l.UpdateStageAsync(Glow.Id, id, It.IsAny<UpdateContactStageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid status."));
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Invalid status.", model.ErrorMessage);
        Assert.NotNull(model.Contact);                                                // the page reloads its data to redisplay

        contacts.Setup(l => l.UpdateStageAsync(Glow.Id, id, It.IsAny<UpdateContactStageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("contacts.errors.unknownStage").WithArgs("vip"));
        await model.OnPostAsync(default);
        Assert.Equal("'vip' isn't one of your contact stages.", model.ErrorMessage); // a keyed message is shown in words

        var none = new Omni.App.Pages.Dashboard.ContactDetailModel(ClientContext(null).Object, contacts.Object, TextKit.Localizer()).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostAsync(default));
    }

    // ---- booking detail ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Booking_detail_shows_status_and_client_time_zone()
    {
        var id = Guid.NewGuid();
        var bookings = new Mock<IBookingService>();
        bookings.Setup(a => a.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BookingResponse>() with { Status = BookingStatus.Confirmed });
        var model = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(SomeClient(timezone: "Asia/Beirut")).Object, bookings.Object) { Id = id }.Attach();
        // the mock is keyed to Glow; use a client context returning the same client instead
        var client = SomeClient(timezone: "Asia/Beirut");
        bookings.Setup(a => a.GetByIdAsync(client.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BookingResponse>() with { Status = BookingStatus.Confirmed });
        model = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(client).Object, bookings.Object) { Id = id }.Attach();
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal((BookingStatus.Confirmed, "Asia/Beirut"), (model.Status, model.ClientTimezone));

        var missing = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(client).Object, bookings.Object) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await missing.OnGetAsync(default));
        var none = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(null).Object, bookings.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
    }

    [Fact]
    public async Task Saving_an_booking_status_redirects_404s_or_shows_the_validation_error()
    {
        var id = Guid.NewGuid();
        var client = SomeClient();
        var bookings = new Mock<IBookingService>();
        var model = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(client).Object, bookings.Object) { Id = id, Status = "attended" }.Attach();

        Assert.IsType<NotFoundResult>(await model.OnPostAsync(default));
        bookings.Setup(a => a.UpdateStatusAsync(client.Id, id, "attended", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BookingResponse>());
        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync(default));

        bookings.Setup(a => a.UpdateStatusAsync(client.Id, id, "attended", It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Bad status."));
        bookings.Setup(a => a.GetByIdAsync(client.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BookingResponse>());
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Bad status.", model.ErrorMessage);

        var none = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(null).Object, bookings.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostAsync(default));
    }
}

public class ServicePageTests
{
    private static readonly Client Glow = SomeClient();

    [Fact]
    public async Task Service_list_loads_and_toggling_reports_the_result()
    {
        var services = new Mock<ICatalogService>();
        services.Setup(p => p.ListAsync(Glow.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ServiceResponse>());
        var id = Guid.NewGuid();
        services.SetupSequence(p => p.SetActiveAsync(Glow.Id, id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceResponse?)null)
            .ReturnsAsync(Blank<ServiceResponse>() with { Name = "Botox" })
            .ReturnsAsync(Blank<ServiceResponse>() with { Name = "Botox" });
        var model = new ServiceIndex(ClientContext(Glow).Object, services.Object).Attach();

        await model.OnGetAsync(default);
        Assert.True(model.ClientConfigured);

        await model.OnPostToggleAsync(id, true, default);
        Assert.Equal("Service not found.", model.ErrorMessage);
        await model.OnPostToggleAsync(id, false, default);
        Assert.Contains("Botox deactivated", model.StatusMessage);
        await model.OnPostToggleAsync(id, true, default);
        Assert.Equal("Botox is active again.", model.StatusMessage);

        var none = new ServiceIndex(ClientContext(null).Object, services.Object).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
        Assert.IsType<RedirectToPageResult>(await none.OnPostToggleAsync(id, true, default));
    }

    [Fact]
    public async Task Service_edit_loads_an_existing_service_or_starts_blank()
    {
        var id = Guid.NewGuid();
        var services = new Mock<ICatalogService>();
        services.Setup(p => p.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<ServiceResponse>() with { Name = "Facelift", Code = "FL", BookingDuration = 45, Description = "d" });
        var edit = new ServiceEdit(ClientContext(Glow).Object, services.Object) { Id = id }.Attach();
        Assert.IsType<PageResult>(await edit.OnGetAsync(default));
        Assert.Equal(("Facelift", "FL", 45, "d", false), (edit.Name, edit.Code, edit.DurationMinutes, edit.Description, edit.IsNew));

        var fresh = new ServiceEdit(ClientContext(Glow).Object, services.Object).Attach();
        Assert.IsType<PageResult>(await fresh.OnGetAsync(default));
        Assert.True(fresh.IsNew);

        var missing = new ServiceEdit(ClientContext(Glow).Object, services.Object) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await missing.OnGetAsync(default));
        var none = new ServiceEdit(ClientContext(null).Object, services.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
        Assert.False(none.ClientConfigured);
    }

    [Fact]
    public async Task Service_edit_creates_updates_and_reports_problems()
    {
        var id = Guid.NewGuid();
        var services = new Mock<ICatalogService>();
        var create = new ServiceEdit(ClientContext(Glow).Object, services.Object) { Name = "Botox", Code = "B", Description = "d", DurationMinutes = 20 }.Attach();
        Assert.Equal("page:/Services/Index", RedirectTarget(await create.OnPostAsync(default)));
        services.Verify(p => p.CreateAsync(new CreateServiceRequest(Glow.Id, "Botox", "B", "d", 20), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Saved.", create.TempData["StatusMessage"]);

        var update = new ServiceEdit(ClientContext(Glow).Object, services.Object) { Id = id, Name = "Botox" }.Attach();
        Assert.IsType<NotFoundResult>(await update.OnPostAsync(default));          // UpdateAsync returns null

        services.Setup(p => p.UpdateAsync(Glow.Id, id, It.IsAny<UpdateServiceRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ServiceResponse>());
        Assert.Equal("page:/Services/Index", RedirectTarget(await update.OnPostAsync(default)));

        services.Setup(p => p.UpdateAsync(Glow.Id, id, It.IsAny<UpdateServiceRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Name taken."));
        Assert.IsType<PageResult>(await update.OnPostAsync(default));
        Assert.Equal("Name taken.", update.ErrorMessage);

        var none = new ServiceEdit(ClientContext(null).Object, services.Object).Attach();
        Assert.Equal("page:/Services/Index", RedirectTarget(await none.OnPostAsync(default)));
    }
}

public class CampaignDetailsPageTests
{
    private static readonly Client Glow = SomeClient();

    [Fact]
    public async Task Campaign_details_load_404_or_show_empty_without_a_client()
    {
        var id = Guid.NewGuid();
        var campaigns = new Mock<ICampaignService>();
        var model = new CampaignDetails(ClientContext(Glow).Object, campaigns.Object).Attach();
        Assert.IsType<NotFoundResult>(await model.OnGetAsync(id, default));

        campaigns.Setup(c => c.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<CampaignDetailsResponse>());
        Assert.IsType<PageResult>(await model.OnGetAsync(id, default));
        Assert.NotNull(model.Details);

        var none = new CampaignDetails(ClientContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(id, default));
        Assert.False(none.ClientConfigured);
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
        var model = new CampaignDetails(ClientContext(Glow).Object, campaigns.Object).Attach();

        Assert.IsType<RedirectToPageResult>(await model.OnPostSendAsync(id, default));
        Assert.Contains("18 sent so far, 2 failed; the other 5 go out automatically", model.StatusMessage);
        await model.OnPostSendAsync(id, default);
        Assert.Equal("Campaign completed: 5 sent, 0 failed.", model.StatusMessage);
        await model.OnPostSendAsync(id, default);
        Assert.Null(model.StatusMessage);
        await model.OnPostSendAsync(id, default);
        Assert.Equal("Not approved yet.", model.ErrorMessage);

        var none = new CampaignDetails(ClientContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostSendAsync(id, default));
    }

    [Fact]
    public async Task Cancelling_reports_success_or_the_error()
    {
        var id = Guid.NewGuid();
        var campaigns = new Mock<ICampaignService>();
        var model = new CampaignDetails(ClientContext(Glow).Object, campaigns.Object).Attach();
        await model.OnPostCancelAsync(id, default);
        Assert.Equal("Campaign cancelled.", model.StatusMessage);

        campaigns.Setup(c => c.CancelAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Already sent."));
        await model.OnPostCancelAsync(id, default);
        Assert.Equal("Already sent.", model.ErrorMessage);

        var none = new CampaignDetails(ClientContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostCancelAsync(id, default));
    }

    [Fact]
    public async Task Stopping_reports_success_or_the_error()
    {
        var id = Guid.NewGuid();
        var campaigns = new Mock<ICampaignService>();
        var model = new CampaignDetails(ClientContext(Glow).Object, campaigns.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await model.OnPostStopAsync(id, default));
        Assert.Contains("Campaign stopped", model.StatusMessage);
        campaigns.Verify(c => c.StopAsync(Glow.Id, id, It.IsAny<CancellationToken>()), Times.Once);

        campaigns.Setup(c => c.StopAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Only a scheduled or sending campaign can be stopped."));
        await model.OnPostStopAsync(id, default);
        Assert.Equal("Only a scheduled or sending campaign can be stopped.", model.ErrorMessage);

        var none = new CampaignDetails(ClientContext(null).Object, campaigns.Object).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostStopAsync(id, default));
        campaigns.Verify(c => c.StopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("paused", "Stopped")]
    [InlineData("running", "Running")]
    [InlineData("scheduled", "Scheduled")]
    public void Campaign_status_labels_call_a_stopped_campaign_stopped(string status, string label) =>
        Assert.Equal(label, StatusBadgeHelper.CampaignStatusLabel(status));
}
