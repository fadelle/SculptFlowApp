using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Business.Mappers.PlatformAdmin;
using PlasticSurgery.Business.Services.Automation;
using PlasticSurgery.Business.Services.PlatformAdmin;

namespace PlasticSurgery.Tests.Services;

public class LeadAdminServiceTests
{
    private readonly Mock<ILeadAdminRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IAppointmentService> _appointments = new();
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly Guid _clinicId = Guid.NewGuid();

    private LeadAdminService Sut() => new(_repo.Object, _uow.Object, _conversations.Object, _appointments.Object, _time);

    [Fact]
    public async Task Lead_update_validates_values_and_tracks_opt_out_transitions()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateLeadAsync(Guid.NewGuid(), new LeadUpdate("bogus", "hot", true, null), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateLeadAsync(Guid.NewGuid(), new LeadUpdate("new", "bogus", true, null), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateLeadAsync(Guid.NewGuid(), new LeadUpdate("new", "hot", true, null), default));

        var lead = new Lead { Id = Guid.NewGuid(), ClinicId = _clinicId, MarketingOptIn = true };
        _repo.Setup(r => r.GetLeadForUpdateAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var change = await Sut().UpdateLeadAsync(lead.Id, new LeadUpdate("qualified", "hot", false, "  call back  "), default);
        Assert.Equal(_clinicId, change.ClinicId);
        Assert.Equal(("qualified", "hot", false, "call back"), (lead.Status, lead.QualificationStatus, lead.MarketingOptIn, lead.Notes));
        Assert.NotNull(lead.OptedOutAt);

        await Sut().UpdateLeadAsync(lead.Id, new LeadUpdate("qualified", "hot", true, " "), default);
        Assert.Null(lead.OptedOutAt);
        Assert.Null(lead.Notes);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Set_mode_only_acts_on_a_real_change_and_rejects_unsupported_modes()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetModeAsync(Guid.NewGuid(), ConversationMode.Approval, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetModeAsync(Guid.NewGuid(), ConversationMode.Ai, default));

        var c = new Conversation { Id = Guid.NewGuid(), ClinicId = _clinicId, Mode = ConversationMode.Ai };
        _repo.Setup(r => r.GetConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        await Sut().SetModeAsync(c.Id, ConversationMode.Ai, default);
        _conversations.Verify(x => x.TakeOverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        await Sut().SetModeAsync(c.Id, ConversationMode.Human, default);
        _conversations.Verify(x => x.TakeOverAsync(_clinicId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        c.Mode = ConversationMode.Human;
        await Sut().SetModeAsync(c.Id, ConversationMode.Ai, default);
        _conversations.Verify(x => x.ReturnToAiAsync(_clinicId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_status_validates_and_skips_no_ops()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetStatusAsync(Guid.NewGuid(), "bogus", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetStatusAsync(Guid.NewGuid(), ConversationStatus.Closed, default));
        var c = new Conversation { Id = Guid.NewGuid(), ClinicId = _clinicId, Status = ConversationStatus.Active };
        _repo.Setup(r => r.GetConversationForUpdateAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        await Sut().SetStatusAsync(c.Id, ConversationStatus.Active, default);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await Sut().SetStatusAsync(c.Id, ConversationStatus.Archived, default);
        Assert.Equal(ConversationStatus.Archived, c.Status);
    }

    [Fact]
    public async Task Appointment_status_goes_through_the_appointment_service_only_when_it_changes()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetAppointmentStatusAsync(Guid.NewGuid(), "attended", default));
        var a = new Appointment { Id = Guid.NewGuid(), ClinicId = _clinicId, Status = AppointmentStatus.Booked };
        _repo.Setup(r => r.GetAppointmentForUpdateAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        await Sut().SetAppointmentStatusAsync(a.Id, AppointmentStatus.Booked, default);
        _appointments.Verify(x => x.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(_clinicId, (await Sut().SetAppointmentStatusAsync(a.Id, AppointmentStatus.Attended, default)).ClinicId);
        _appointments.Verify(x => x.UpdateStatusAsync(_clinicId, a.Id, AppointmentStatus.Attended, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reads_map_entities_and_clamp_message_counts()
    {
        Assert.Null(await Sut().GetLeadAsync(Guid.NewGuid(), default));
        var lead = new Lead { Id = Guid.NewGuid(), ClinicId = _clinicId, FullName = "Ann", Clinic = new Clinic { Id = _clinicId, Name = "C" }, Procedure = new Procedure { Id = Guid.NewGuid(), Name = "Rhino" } };
        _repo.Setup(r => r.GetLeadAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var detail = await Sut().GetLeadAsync(lead.Id, default);
        Assert.Equal(("Ann", "C", "Rhino"), (detail!.FullName, detail.Clinic!.Name, detail.Procedure!.Name));

        _repo.Setup(r => r.LeadEventsAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<EventLog> { new() { Id = Guid.NewGuid(), EventType = "x", Metadata = "{}" } });
        Assert.Single(await Sut().LeadEventsAsync(lead.Id, default));

        Assert.Null(await Sut().GetConversationAsync(Guid.NewGuid(), default));
        var c = new Conversation { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = "whatsapp", ServiceWindowExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Lead = lead };
        _repo.Setup(r => r.GetConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        Assert.True((await Sut().GetConversationAsync(c.Id, default))!.IsServiceWindowOpen);

        _repo.Setup(r => r.MessagesAsync(c.Id, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message> { new() { Id = Guid.NewGuid() } });
        Assert.Single(await Sut().MessagesAsync(c.Id, 99999, default));
        _repo.Setup(r => r.MessagesAsync(c.Id, 1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message>());
        Assert.Empty(await Sut().MessagesAsync(c.Id, -4, default));
    }

    [Fact]
    public async Task List_methods_delegate()
    {
        await Sut().ListLeadsAsync(_clinicId, "new", "q", 2, default);
        await Sut().ListConversationsAsync(_clinicId, null, "whatsapp", "ai", "active", 1, default);
        await Sut().ListMessagesAsync(_clinicId, true, "ai", "q", 3, default);
        await Sut().ListAppointmentsAsync(_clinicId, null, "booked", true, 1, default);
        _repo.Verify(r => r.ListLeadsAsync(_clinicId, "new", "q", 2, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.ListMessagesAsync(_clinicId, true, "ai", "q", 3, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ClinicAdminServiceTests
{
    private readonly Mock<IClinicAdminRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ClinicAdminService Sut() => new(_repo.Object, _uow.Object, TimeProvider.System);

    private static ClinicUpdate Update(string name = " Glow ", string tz = "Asia/Beirut") => new(name, " 961 ", " ", "https://x", "lb", tz, " Street ", null, "info");

    [Fact]
    public async Task Update_trims_cleans_and_uppercases_the_country()
    {
        var clinic = new Clinic { Id = Guid.NewGuid(), Name = "Old" };
        _repo.Setup(r => r.GetForUpdateAsync(clinic.Id, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        var change = await Sut().UpdateAsync(clinic.Id, Update(), default);
        Assert.Equal(clinic.Id, change.ClinicId);
        Assert.Equal(("Glow", "961", null, "LB", "Asia/Beirut", "Street"), (clinic.Name, clinic.Phone, clinic.Email, clinic.CountryCode, clinic.Timezone, clinic.Address));
        await Sut().UpdateAsync(clinic.Id, Update(tz: " "), default);
        Assert.Equal("UTC", clinic.Timezone);
    }

    [Fact]
    public async Task Update_validates_name_zone_and_existence()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(Guid.NewGuid(), Update(name: " "), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(Guid.NewGuid(), Update(tz: "Mars/Base"), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAsync(Guid.NewGuid(), Update(), default));
    }

    [Fact]
    public async Task Activation_is_idempotent()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetActiveAsync(Guid.NewGuid(), false, default));
        var clinic = new Clinic { Id = Guid.NewGuid(), IsActive = true };
        _repo.Setup(r => r.GetForUpdateAsync(clinic.Id, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        await Sut().SetActiveAsync(clinic.Id, true, default);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await Sut().SetActiveAsync(clinic.Id, false, default);
        Assert.False(clinic.IsActive);
    }

    [Fact]
    public async Task Reads_delegate_and_map()
    {
        Assert.Null(await Sut().GetAsync(Guid.NewGuid(), default));
        var clinic = new Clinic { Id = Guid.NewGuid(), Name = "C", Slug = "c" };
        _repo.Setup(r => r.GetAsync(clinic.Id, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        Assert.Equal("c", (await Sut().GetAsync(clinic.Id, default))!.Slug);
        await Sut().ListAsync("q", true, 1, default);
        await Sut().OptionsAsync(default);
        await Sut().CountsAsync(clinic.Id, default);
        _repo.Verify(r => r.ListAsync("q", true, 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class StaffAdminServiceTests
{
    private readonly Mock<IStaffAdminRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<UserManager<IdentityUser>> _users;
    private readonly IdentityUser _user = new() { Id = "u1", UserName = "a@x.com" };
    private readonly Guid _clinicId = Guid.NewGuid();

    public StaffAdminServiceTests()
    {
        _users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(_user);
        _users.Setup(u => u.SetLockoutEnabledAsync(_user, true)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.SetLockoutEndDateAsync(_user, It.IsAny<DateTimeOffset?>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.ResetAccessFailedCountAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateSecurityStampAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.HasPasswordAsync(_user)).ReturnsAsync(true);
        _users.Setup(u => u.RemovePasswordAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddPasswordAsync(_user, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _repo.Setup(r => r.ClinicOfAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);
    }

    private StaffAdminService Sut() => new(_repo.Object, _uow.Object, _users.Object, TimeProvider.System);

    [Fact]
    public async Task Membership_toggle_rules()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetMembershipActiveAsync("u1", true, default));
        var m = new ClinicUser { Id = Guid.NewGuid(), ClinicId = _clinicId, IsActive = false };
        _repo.Setup(r => r.GetLatestMembershipAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(m);
        _repo.Setup(r => r.HasOtherActiveMembershipAsync("u1", m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetMembershipActiveAsync("u1", true, default));
        Assert.False(m.IsActive);

        _repo.Setup(r => r.HasOtherActiveMembershipAsync("u1", m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.Equal(_clinicId, (await Sut().SetMembershipActiveAsync("u1", true, default)).ClinicId);
        Assert.True(m.IsActive);
        _uow.Invocations.Clear();
        await Sut().SetMembershipActiveAsync("u1", true, default); // unchanged → no save
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Locking_sets_an_open_ended_lockout_and_revokes_sessions_unlocking_clears_it()
    {
        await Sut().SetLoginLockedAsync("u1", true, default);
        _users.Verify(u => u.SetLockoutEndDateAsync(_user, DateTimeOffset.MaxValue), Times.Once);
        _users.Verify(u => u.UpdateSecurityStampAsync(_user), Times.Once);
        await Sut().SetLoginLockedAsync("u1", false, default);
        _users.Verify(u => u.SetLockoutEndDateAsync(_user, null), Times.Once);
        _users.Verify(u => u.UpdateSecurityStampAsync(_user), Times.Once);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetLoginLockedAsync("nobody", true, default));
    }

    [Fact]
    public async Task Identity_failures_become_argument_errors()
    {
        _users.Setup(u => u.SetLockoutEndDateAsync(_user, It.IsAny<DateTimeOffset?>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "store down" }));
        Assert.Contains("store down", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetLoginLockedAsync("u1", true, default))).Message);
    }

    [Fact]
    public async Task Password_reset_validates_replaces_and_resets_failures()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetPasswordAsync("u1", "", default));
        var validator = new Mock<IPasswordValidator<IdentityUser>>();
        validator.Setup(v => v.ValidateAsync(_users.Object, _user, "weak")).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "too weak" }));
        validator.Setup(v => v.ValidateAsync(_users.Object, _user, "Str0ng!pass")).ReturnsAsync(IdentityResult.Success);
        _users.Object.PasswordValidators.Add(validator.Object);
        Assert.Contains("too weak", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetPasswordAsync("u1", "weak", default))).Message);
        _users.Verify(u => u.RemovePasswordAsync(_user), Times.Never);

        var change = await Sut().SetPasswordAsync("u1", "Str0ng!pass", default);
        Assert.Equal(_clinicId, change.ClinicId);
        _users.Verify(u => u.RemovePasswordAsync(_user), Times.Once);
        _users.Verify(u => u.AddPasswordAsync(_user, "Str0ng!pass"), Times.Once);

        _users.Setup(u => u.HasPasswordAsync(_user)).ReturnsAsync(false); // Google-only account gets a password added
        _users.Invocations.Clear();
        await Sut().SetPasswordAsync("u1", "Str0ng!pass", default);
        _users.Verify(u => u.RemovePasswordAsync(_user), Times.Never);
    }

    [Fact]
    public async Task Email_confirmation_and_reads()
    {
        await Sut().SetEmailConfirmedAsync("u1", true, default);
        Assert.True(_user.EmailConfirmed);
        _users.Verify(u => u.UpdateAsync(_user), Times.Once);
        await Sut().ListAsync(_clinicId, "q", 1, default);
        await Sut().GetAsync("u1", default);
        _repo.Verify(r => r.ListAsync(_clinicId, "q", 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ChannelAdminServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelAdminRepository> _repo = new();
    private readonly Mock<IChannelIntegrationService> _channels = new();
    private readonly Mock<IInfobipWhatsAppIntegrationService> _infobip = new();
    private readonly Mock<ICalendarIntegrationService> _calendars = new();
    private readonly Mock<ITikTokIntegrationService> _tikTok = new();

    private ChannelAdminService Sut() => new(_repo.Object, _channels.Object, _infobip.Object, _calendars.Object, _tikTok.Object);

    [Fact]
    public async Task Channel_detail_includes_the_infobip_webhook_url_only_for_connected_infobip_numbers()
    {
        Assert.Null(await Sut().GetChannelAsync(Guid.NewGuid(), default));
        var row = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, Provider = ChannelProvider.Infobip, AccessToken = "secret" };
        _repo.Setup(r => r.GetChannelAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _infobip.Setup(i => i.GetWebhookUrlAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync("https://hook");
        var detail = await Sut().GetChannelAsync(row.Id, default);
        Assert.Equal(("https://hook", true), (detail!.InfobipWebhookUrl, detail.HasAccessToken));
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(detail));

        row.Provider = ChannelProvider.Meta;
        Assert.Null((await Sut().GetChannelAsync(row.Id, default))!.InfobipWebhookUrl);
    }

    [Fact]
    public async Task Disconnects_delegate_to_the_clinic_services_so_rules_run_once()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DisconnectAsync(Guid.NewGuid(), default));
        var row = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.Telegram };
        _repo.Setup(r => r.GetChannelAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        Assert.Equal(_clinicId, (await Sut().DisconnectAsync(row.Id, default)).ClinicId);
        _channels.Verify(c => c.DisconnectAsync(_clinicId, ChannelType.Telegram, It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DisconnectCalendarAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetCalendarSyncAsync(Guid.NewGuid(), true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DisconnectTikTokAsync(Guid.NewGuid(), default));
        var cal = new CalendarIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Provider = CalendarProvider.Google };
        _repo.Setup(r => r.GetCalendarAsync(cal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cal);
        await Sut().DisconnectCalendarAsync(cal.Id, default);
        await Sut().SetCalendarSyncAsync(cal.Id, true, default);
        _calendars.Verify(c => c.DisconnectAsync(_clinicId, CalendarProvider.Google, It.IsAny<CancellationToken>()), Times.Once);
        _calendars.Verify(c => c.SetSyncEnabledAsync(_clinicId, CalendarProvider.Google, true, It.IsAny<CancellationToken>()), Times.Once);
        var tt = new TikTokIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId };
        _repo.Setup(r => r.GetTikTokAsync(tt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tt);
        await Sut().DisconnectTikTokAsync(tt.Id, default);
        _tikTok.Verify(t => t.DisconnectAsync(_clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Connecting_an_infobip_sender_returns_the_new_detail()
    {
        var row = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, Provider = ChannelProvider.Infobip };
        _infobip.Setup(i => i.ConnectAsync(_clinicId, "+447", It.IsAny<CancellationToken>())).ReturnsAsync(new ChannelIntegrationResponse(row.Id, _clinicId, "whatsapp", "connected", null, null, null, null, null, true, true, null, null, DateTimeOffset.UtcNow));
        _repo.Setup(r => r.GetChannelAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        Assert.Equal(row.Id, (await Sut().ConnectInfobipSenderAsync(_clinicId, "+447", default)).Id);
        await Sut().ListChannelsAsync(_clinicId, null, true, default);
        await Sut().ListCalendarsAsync(_clinicId, default);
        await Sut().ListTikTokAsync(_clinicId, default);
        _repo.Setup(r => r.HealthEventsAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEvent> { new() { Id = Guid.NewGuid(), EventType = "x", Severity = "info" } });
        Assert.Single(await Sut().HealthEventsAsync(row.Id, default));
    }
}

public class ContentAndOverviewAdminServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IContentAdminRepository> _repo = new();
    private readonly Mock<ICampaignService> _campaigns = new();
    private readonly Mock<IProcedureService> _procedures = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();

    private ContentAdminService Sut() => new(_repo.Object, _campaigns.Object, _procedures.Object, _knowledge.Object, _settings.Object);

    [Fact]
    public async Task Campaign_cancel_goes_through_the_campaign_service()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CancelCampaignAsync(Guid.NewGuid(), default));
        var row = new CampaignRow(Guid.NewGuid(), _clinicId, "C", "n", "t", "whatsapp", "draft", null, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, DateTimeOffset.UtcNow);
        _repo.Setup(r => r.GetCampaignAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CancelCampaignAsync(row.Id, default)); // service says not found
    }

    [Fact]
    public async Task Procedure_document_and_settings_changes_use_the_clinic_services()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetProcedureActiveAsync(Guid.NewGuid(), true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetDocumentActiveAsync(Guid.NewGuid(), true, default));
        var proc = new Procedure { Id = Guid.NewGuid(), ClinicId = _clinicId };
        _repo.Setup(r => r.GetProcedureAsync(proc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(proc);
        Assert.Equal(_clinicId, (await Sut().SetProcedureActiveAsync(proc.Id, false, default)).ClinicId);
        _procedures.Verify(p => p.SetActiveAsync(_clinicId, proc.Id, false, It.IsAny<CancellationToken>()), Times.Once);

        var doc = new KnowledgeDocument { Id = Guid.NewGuid(), ClinicId = _clinicId, Title = "T", Clinic = new Clinic { Id = _clinicId, Name = "C" } };
        _repo.Setup(r => r.GetDocumentAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        await Sut().SetDocumentActiveAsync(doc.Id, true, default);
        _knowledge.Verify(k => k.SetActiveAsync(_clinicId, doc.Id, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("C", (await Sut().GetDocumentAsync(doc.Id, default))!.Clinic!.Name);
        Assert.Null(await Sut().GetDocumentAsync(Guid.NewGuid(), default));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().UpdateSearchSettingsAsync(_clinicId, 4, 0.4, default));
        _repo.Setup(r => r.SearchSettingsAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSearchSettings { Id = Guid.NewGuid(), ClinicId = _clinicId, ChunkSizeTokens = 300, ChunkOverlapTokens = 30 });
        await Sut().UpdateSearchSettingsAsync(_clinicId, 4, 0.4, default);
        _settings.Verify(s => s.UpdateAsync(_clinicId, new UpdateKnowledgeSettingsRequest(300, 30, 4, 0.4), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(300, (await Sut().SearchSettingsAsync(_clinicId, default))!.ChunkSizeTokens);
        Assert.Null(await Sut().SearchSettingsAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Listings_delegate_and_overview_clamps()
    {
        await Sut().ListCampaignsAsync(_clinicId, "draft", 1, default);
        await Sut().GetCampaignAsync(Guid.NewGuid(), default);
        await Sut().RecipientsAsync(Guid.NewGuid(), null, 1, default);
        await Sut().ListTemplatesAsync(_clinicId, null, default);
        await Sut().ListProceduresAsync(_clinicId, default);
        await Sut().ListDocumentsAsync(_clinicId, "q", true, 1, default);
        await Sut().ListWebsitesAsync(_clinicId, default);
        _repo.Verify(r => r.ListCampaignsAsync(_clinicId, "draft", 1, It.IsAny<CancellationToken>()), Times.Once);

        var overview = new Mock<IOverviewAdminRepository>();
        var sut = new OverviewAdminService(overview.Object);
        await sut.TotalsAsync(default);
        await sut.DailyMessagesAsync(1000, default);
        await sut.DailyMessagesAsync(0, default);
        await sut.RecentClinicsAsync(500, default);
        await sut.RecentClinicsAsync(-1, default);
        await sut.ProblemsAsync(default);
        await sut.EventsAsync(null, "x", 1, default);
        await sut.EventTypesAsync(default);
        overview.Verify(o => o.DailyMessagesAsync(90, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(o => o.DailyMessagesAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(o => o.RecentClinicsAsync(100, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(o => o.RecentClinicsAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class AutomationCleanupServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<ICacheManager> _cache = new();

    public AutomationCleanupServiceTests() => _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);

    private AutomationCleanupService Sut() => new(_clinics.Object, _users.Object, _uow.Object, _cache.Object);

    private void Setup(string clinicName, params string[] emails)
    {
        _clinics.Setup(c => c.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new Clinic { Id = _clinicId, Name = clinicName });
        _clinics.Setup(c => c.ListMemberUserIdsAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "u1" });
        _users.Setup(u => u.ListEmailsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(emails);
    }

    [Fact]
    public async Task Only_clinics_that_are_clearly_automation_data_can_be_deleted()
    {
        Assert.Equal(AutomationDeleteOutcome.NotFound, await Sut().DeleteClinicAsync(Guid.NewGuid()));

        Setup("[Automation] run 1", "bot@sculptflow-automation.test");
        Assert.Equal(AutomationDeleteOutcome.Deleted, await Sut().DeleteClinicAsync(_clinicId));
        _clinics.Verify(c => c.DeleteAsync(_clinicId, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.DeleteAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(CacheKeys.Entitlements(_clinicId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Real Clinic", "bot@sculptflow-automation.test")]
    [InlineData("[Automation] run", "bot@sculptflow-automation.test", "doctor@real.com")]
    [InlineData("[Automation] run")]
    [InlineData("automation run", "bot@sculptflow-automation.test")]
    public async Task Anything_else_is_refused_and_nothing_is_deleted(string name, params string[] emails)
    {
        Setup(name, emails);
        Assert.Equal(AutomationDeleteOutcome.NotAutomationClinic, await Sut().DeleteClinicAsync(_clinicId));
        _clinics.Verify(c => c.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(u => u.DeleteAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listing_uses_the_automation_prefix()
    {
        await Sut().ListClinicsAsync();
        _clinics.Verify(c => c.ListByNamePrefixAsync(AutomationClinics.NamePrefix, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class PlatformAdminMapperTests
{
    [Fact]
    public void Mappers_copy_fields_and_never_expose_the_access_token()
    {
        var clinic = new Clinic { Id = Guid.NewGuid(), Name = "C", Slug = "c", Timezone = "UTC" };
        var channel = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = clinic.Id, Clinic = clinic, Channel = "whatsapp", AccessToken = "secret", Provider = "infobip" };
        var detail = PlatformAdminMapper.ToDetail(channel, "https://hook");
        Assert.Equal((true, "C", "https://hook"), (detail.HasAccessToken, detail.Clinic!.Name, detail.InfobipWebhookUrl));
        Assert.False(PlatformAdminMapper.ToDetail(new ChannelIntegration { Channel = "whatsapp" }, null).HasAccessToken);

        var ev = new WhatsAppHealthEvent { Id = Guid.NewGuid(), EventType = "x", Severity = "info" };
        Assert.Equal("x", PlatformAdminMapper.ToRow(ev).EventType);
        var log = new EventLog { Id = Guid.NewGuid(), EventType = "e", Metadata = "{}" };
        Assert.Equal("e", PlatformAdminMapper.ToRow(log).EventType);
        Assert.Equal("c", PlatformAdminMapper.ToDetail(clinic).Slug);
        var settings = new KnowledgeSearchSettings { Id = Guid.NewGuid(), TopK = 4 };
        Assert.Equal(4, PlatformAdminMapper.ToDetail(settings).TopK);
        var msg = new Message { Id = Guid.NewGuid(), Content = "hi", Direction = "inbound" };
        Assert.Equal("hi", PlatformAdminMapper.ToDetail(msg).Content);
        var convo = new Conversation { Id = Guid.NewGuid(), Channel = "whatsapp" };
        Assert.Null(PlatformAdminMapper.ToDetail(convo, DateTimeOffset.UtcNow).Lead);
    }
}
