using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Business.Services.Automation;
using Omni.App.Business.Services.PlatformAdmin;

namespace Omni.App.Tests.Services;

public class ContactAdminServiceTests
{
    private readonly Mock<IContactAdminRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IBookingService> _bookings = new();
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly Guid _clientId = Guid.NewGuid();

    private readonly Mock<IBusinessTypeService> _businessTypes = TextKit.PermissiveBusinessTypes();

    private ContactAdminService Sut() => new(_repo.Object, _uow.Object, _conversations.Object, _bookings.Object, _time, _businessTypes.Object);

    [Fact]
    public async Task Contact_update_validates_values_and_tracks_opt_out_transitions()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateContactAsync(Guid.NewGuid(), new ContactUpdate("new", "bogus", true, null), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateContactAsync(Guid.NewGuid(), new ContactUpdate("new", "hot", true, null), default));

        var contact = new Contact { Id = Guid.NewGuid(), ClientId = _clientId, MarketingOptIn = true };
        _repo.Setup(r => r.GetContactForUpdateAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        // The status must be a stage of the contact's client's business type.
        _businessTypes.Setup(t => t.IsStageOfClientAsync(_clientId, "bogus", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var unknown = await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateContactAsync(contact.Id, new ContactUpdate("bogus", "hot", true, null), default));
        Assert.Equal("'bogus' isn't one of your contact stages.", TextKit.Localizer().Localize(unknown));
        var change = await Sut().UpdateContactAsync(contact.Id, new ContactUpdate("qualified", "hot", false, "  call back  "), default);
        Assert.Equal(_clientId, change.ClientId);
        Assert.Equal(("qualified", "hot", false, "call back"), (contact.StageKey, contact.Qualification, contact.MarketingOptIn, contact.Notes));
        Assert.NotNull(contact.OptedOutAt);

        await Sut().UpdateContactAsync(contact.Id, new ContactUpdate("qualified", "hot", true, " "), default);
        Assert.Null(contact.OptedOutAt);
        Assert.Null(contact.Notes);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Set_mode_only_acts_on_a_real_change_and_rejects_unsupported_modes()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetModeAsync(Guid.NewGuid(), ConversationMode.Approval, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetModeAsync(Guid.NewGuid(), ConversationMode.Ai, default));

        var c = new Conversation { Id = Guid.NewGuid(), ClientId = _clientId, Mode = ConversationMode.Ai };
        _repo.Setup(r => r.GetConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        await Sut().SetModeAsync(c.Id, ConversationMode.Ai, default);
        _conversations.Verify(x => x.TakeOverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        await Sut().SetModeAsync(c.Id, ConversationMode.Human, default);
        _conversations.Verify(x => x.TakeOverAsync(_clientId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        c.Mode = ConversationMode.Human;
        await Sut().SetModeAsync(c.Id, ConversationMode.Ai, default);
        _conversations.Verify(x => x.ReturnToAiAsync(_clientId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_status_validates_and_skips_no_ops()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetStatusAsync(Guid.NewGuid(), "bogus", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetStatusAsync(Guid.NewGuid(), ConversationStatus.Closed, default));
        var c = new Conversation { Id = Guid.NewGuid(), ClientId = _clientId, Status = ConversationStatus.Active };
        _repo.Setup(r => r.GetConversationForUpdateAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        await Sut().SetStatusAsync(c.Id, ConversationStatus.Active, default);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await Sut().SetStatusAsync(c.Id, ConversationStatus.Archived, default);
        Assert.Equal(ConversationStatus.Archived, c.Status);
    }

    [Fact]
    public async Task Booking_status_goes_through_the_booking_service_only_when_it_changes()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetBookingStatusAsync(Guid.NewGuid(), "attended", default));
        var a = new Booking { Id = Guid.NewGuid(), ClientId = _clientId, Status = BookingStatus.Booked };
        _repo.Setup(r => r.GetBookingForUpdateAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        await Sut().SetBookingStatusAsync(a.Id, BookingStatus.Booked, default);
        _bookings.Verify(x => x.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(_clientId, (await Sut().SetBookingStatusAsync(a.Id, BookingStatus.Attended, default)).ClientId);
        _bookings.Verify(x => x.UpdateStatusAsync(_clientId, a.Id, BookingStatus.Attended, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reads_map_entities_and_clamp_message_counts()
    {
        Assert.Null(await Sut().GetContactAsync(Guid.NewGuid(), default));
        var contact = new Contact { Id = Guid.NewGuid(), ClientId = _clientId, FullName = "Ann", Client = new Client { Id = _clientId, Name = "C" }, Service = new Service { Id = Guid.NewGuid(), Name = "Rhino" } };
        _repo.Setup(r => r.GetContactAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var detail = await Sut().GetContactAsync(contact.Id, default);
        Assert.Equal(("Ann", "C", "Rhino"), (detail!.FullName, detail.Client!.Name, detail.Service!.Name));

        _repo.Setup(r => r.ContactEventsAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<EventLog> { new() { Id = Guid.NewGuid(), EventType = "x", Metadata = "{}" } });
        Assert.Single(await Sut().ContactEventsAsync(contact.Id, default));

        Assert.Null(await Sut().GetConversationAsync(Guid.NewGuid(), default));
        var c = new Conversation { Id = Guid.NewGuid(), ClientId = _clientId, Channel = "whatsapp", ServiceWindowExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Contact = contact };
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
        await Sut().ListContactsAsync(_clientId, "new", "q", 2, default);
        await Sut().ListConversationsAsync(_clientId, null, "whatsapp", "ai", "active", 1, default);
        await Sut().ListMessagesAsync(_clientId, true, "ai", "q", 3, default);
        await Sut().ListBookingsAsync(_clientId, null, "booked", true, 1, default);
        _repo.Verify(r => r.ListContactsAsync(_clientId, "new", "q", 2, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.ListMessagesAsync(_clientId, true, "ai", "q", 3, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ClientAdminServiceTests
{
    private readonly Mock<IClientAdminRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxSettingsService> _inboxSettings = new();

    private readonly Mock<IBusinessTypeService> _businessTypes = new();
    private readonly Mock<IClientProfileService> _profile = new();

    private ClientAdminService Sut() => new(_repo.Object, _uow.Object, TimeProvider.System, _inboxSettings.Object, _businessTypes.Object, _profile.Object);

    [Fact]
    public async Task Business_type_preview_and_change_go_through_the_business_type_service()
    {
        var id = Guid.NewGuid();
        var preview = new BusinessTypeChangePreview(id, TextKit.Clinic, TextKit.General, 0, []);
        _businessTypes.Setup(t => t.PreviewClientChangeAsync(id, TextKit.General, It.IsAny<CancellationToken>())).ReturnsAsync(preview);
        _businessTypes.Setup(t => t.ChangeClientTypeAsync(id, TextKit.General, It.IsAny<CancellationToken>())).ReturnsAsync(preview).Verifiable();

        Assert.Same(preview, await Sut().PreviewBusinessTypeChangeAsync(id, TextKit.General, default));
        Assert.Equal(id, (await Sut().ChangeBusinessTypeAsync(id, TextKit.General, default)).ClientId);
        _businessTypes.Verify();
    }

    [Fact]
    public async Task The_default_language_goes_through_the_client_profile_service()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, (await Sut().SetDefaultLanguageAsync(id, "ar", default)).ClientId);
        _profile.Verify(p => p.SetDefaultLanguageAsync(id, "ar", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ai_bot_reads_and_writes_go_through_the_client_ai_bot_service()
    {
        var id = Guid.NewGuid();
        _inboxSettings.Setup(a => a.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new InboxSettingsDto(true, true, false));
        Assert.False((await Sut().GetInboxSettingsAsync(id, default)).AiBotReopenedConversations);

        var change = await Sut().UpdateInboxSettingsAsync(id, new UpdateInboxSettingsRequest(false, true), default);
        Assert.Equal(id, change.ClientId);
        _inboxSettings.Verify(a => a.UpdateAsync(id, new UpdateInboxSettingsRequest(false, true), It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateInboxSettingsAsync(id, null!, default));
    }

    private static ClientUpdate Update(string name = " Glow ", string tz = "Asia/Beirut") => new(name, " 961 ", " ", "https://x", "lb", tz, " Street ", null, "info");

    [Fact]
    public async Task Update_trims_cleans_and_uppercases_the_country()
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "Old" };
        _repo.Setup(r => r.GetForUpdateAsync(client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var change = await Sut().UpdateAsync(client.Id, Update(), default);
        Assert.Equal(client.Id, change.ClientId);
        Assert.Equal(("Glow", "961", null, "LB", "Asia/Beirut", "Street"), (client.Name, client.Phone, client.Email, client.CountryCode, client.Timezone, client.Address));
        await Sut().UpdateAsync(client.Id, Update(tz: " "), default);
        Assert.Equal("UTC", client.Timezone);
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
        var client = new Client { Id = Guid.NewGuid(), IsActive = true };
        _repo.Setup(r => r.GetForUpdateAsync(client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        await Sut().SetActiveAsync(client.Id, true, default);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await Sut().SetActiveAsync(client.Id, false, default);
        Assert.False(client.IsActive);
    }

    [Fact]
    public async Task Reads_delegate_and_map()
    {
        Assert.Null(await Sut().GetAsync(Guid.NewGuid(), default));
        var client = new Client { Id = Guid.NewGuid(), Name = "C", Slug = "c" };
        _repo.Setup(r => r.GetAsync(client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        Assert.Equal("c", (await Sut().GetAsync(client.Id, default))!.Slug);
        await Sut().ListAsync("q", true, 1, default);
        await Sut().OptionsAsync(default);
        await Sut().CountsAsync(client.Id, default);
        _repo.Verify(r => r.ListAsync("q", true, 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class MemberAdminServiceTests
{
    private readonly Mock<IMemberAdminRepository> _repo = new();
    private readonly Mock<IMemberService> _members = new();
    private readonly Mock<UserManager<AppUser>> _users;
    private readonly AppUser _user = new() { Id = "u1", UserName = "a@x.com", Role = UserRole.Member };
    private readonly AppUser _admin = new() { Id = "admin-1", UserName = "ad@x.com", Role = UserRole.Admin };
    private readonly Guid _clientId = Guid.NewGuid();

    public MemberAdminServiceTests()
    {
        _users = new Mock<UserManager<AppUser>>(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(_user);
        _users.Setup(u => u.FindByIdAsync("admin-1")).ReturnsAsync(_admin);
        _users.Setup(u => u.SetLockoutEnabledAsync(_user, true)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.SetLockoutEndDateAsync(_user, It.IsAny<DateTimeOffset?>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.ResetAccessFailedCountAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateSecurityStampAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.HasPasswordAsync(_user)).ReturnsAsync(true);
        _users.Setup(u => u.RemovePasswordAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddPasswordAsync(_user, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _repo.Setup(r => r.ClientOfAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(_clientId);
    }

    private MemberAdminService Sut() => new(_repo.Object, _members.Object, _users.Object);

    [Fact]
    public async Task Membership_toggle_goes_through_the_member_rules()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetMembershipActiveAsync("u1", true, default));
        var m = new ClientUser { Id = Guid.NewGuid(), ClientId = _clientId, IsActive = false };
        _repo.Setup(r => r.GetLatestMembershipAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(m);
        _members.Setup(x => x.SetMembershipActiveAsync(m, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("members.errors.otherClient"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetMembershipActiveAsync("u1", true, default));

        _members.Setup(x => x.SetMembershipActiveAsync(m, true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Assert.Equal(_clientId, (await Sut().SetMembershipActiveAsync("u1", true, default)).ClientId);
        _members.Verify(x => x.SetMembershipActiveAsync(m, true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Admin_users_are_not_managed_through_the_members_api()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetLoginLockedAsync("admin-1", true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetPasswordAsync("admin-1", "Str0ng!pass", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetEmailConfirmedAsync("admin-1", true, default));
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
        var validator = new Mock<IPasswordValidator<AppUser>>();
        validator.Setup(v => v.ValidateAsync(_users.Object, _user, "weak")).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "too weak" }));
        validator.Setup(v => v.ValidateAsync(_users.Object, _user, "Str0ng!pass")).ReturnsAsync(IdentityResult.Success);
        _users.Object.PasswordValidators.Add(validator.Object);
        Assert.Contains("too weak", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetPasswordAsync("u1", "weak", default))).Message);
        _users.Verify(u => u.RemovePasswordAsync(_user), Times.Never);

        var change = await Sut().SetPasswordAsync("u1", "Str0ng!pass", default);
        Assert.Equal(_clientId, change.ClientId);
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
        await Sut().ListAsync(_clientId, "q", 1, default);
        await Sut().GetAsync("u1", default);
        _repo.Verify(r => r.ListAsync(_clientId, "q", 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ChannelAdminServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IChannelAdminRepository> _repo = new();
    private readonly Mock<IChannelIntegrationService> _channels = new();
    private readonly Mock<ICalendarIntegrationService> _calendars = new();
    private readonly Mock<ITikTokIntegrationService> _tikTok = new();

    private ChannelAdminService Sut() => new(_repo.Object, _channels.Object, _calendars.Object, _tikTok.Object, Mock.Of<IMetaGraphClient>());

    [Fact]
    public async Task Channel_detail_never_exposes_the_access_token()
    {
        Assert.Null(await Sut().GetChannelAsync(Guid.NewGuid(), default));
        var row = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, AccessToken = "secret" };
        _repo.Setup(r => r.GetChannelAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var detail = await Sut().GetChannelAsync(row.Id, default);
        Assert.True(detail!.HasAccessToken);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(detail));
    }

    [Fact]
    public async Task Disconnects_delegate_to_the_client_services_so_rules_run_once()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DisconnectAsync(Guid.NewGuid(), default));
        var row = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.Telegram };
        _repo.Setup(r => r.GetChannelAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        Assert.Equal(_clientId, (await Sut().DisconnectAsync(row.Id, default)).ClientId);
        _channels.Verify(c => c.DisconnectAsync(_clientId, ChannelType.Telegram, It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DisconnectCalendarAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetCalendarSyncAsync(Guid.NewGuid(), true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DisconnectTikTokAsync(Guid.NewGuid(), default));
        var cal = new CalendarIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Provider = CalendarProvider.Google };
        _repo.Setup(r => r.GetCalendarAsync(cal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cal);
        await Sut().DisconnectCalendarAsync(cal.Id, default);
        await Sut().SetCalendarSyncAsync(cal.Id, true, default);
        _calendars.Verify(c => c.DisconnectAsync(_clientId, CalendarProvider.Google, It.IsAny<CancellationToken>()), Times.Once);
        _calendars.Verify(c => c.SetSyncEnabledAsync(_clientId, CalendarProvider.Google, true, It.IsAny<CancellationToken>()), Times.Once);
        var tt = new TikTokIntegration { Id = Guid.NewGuid(), ClientId = _clientId };
        _repo.Setup(r => r.GetTikTokAsync(tt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tt);
        await Sut().DisconnectTikTokAsync(tt.Id, default);
        _tikTok.Verify(t => t.DisconnectAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lists_and_health_events_delegate_to_the_repository()
    {
        var row = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected };
        await Sut().ListChannelsAsync(_clientId, null, true, default);
        await Sut().ListCalendarsAsync(_clientId, default);
        await Sut().ListTikTokAsync(_clientId, default);
        _repo.Setup(r => r.HealthEventsAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEvent> { new() { Id = Guid.NewGuid(), EventType = "x", Severity = "info" } });
        Assert.Single(await Sut().HealthEventsAsync(row.Id, default));
    }
}

public class ContentAndOverviewAdminServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IContentAdminRepository> _repo = new();
    private readonly Mock<ICampaignService> _campaigns = new();
    private readonly Mock<ICatalogService> _services = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();

    private ContentAdminService Sut() => new(_repo.Object, _campaigns.Object, _services.Object, _knowledge.Object, _settings.Object);

    [Fact]
    public async Task Campaign_cancel_goes_through_the_campaign_service()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CancelCampaignAsync(Guid.NewGuid(), default));
        var row = new CampaignRow(Guid.NewGuid(), _clientId, "C", "n", "t", "whatsapp", "draft", null, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, DateTimeOffset.UtcNow);
        _repo.Setup(r => r.GetCampaignAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CancelCampaignAsync(row.Id, default)); // service says not found
    }

    [Fact]
    public async Task Service_document_and_settings_changes_use_the_client_services()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetServiceActiveAsync(Guid.NewGuid(), true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetDocumentActiveAsync(Guid.NewGuid(), true, default));
        var svc = new Service { Id = Guid.NewGuid(), ClientId = _clientId };
        _repo.Setup(r => r.GetServiceAsync(svc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(svc);
        Assert.Equal(_clientId, (await Sut().SetServiceActiveAsync(svc.Id, false, default)).ClientId);
        _services.Verify(p => p.SetActiveAsync(_clientId, svc.Id, false, It.IsAny<CancellationToken>()), Times.Once);

        var doc = new KnowledgeDocument { Id = Guid.NewGuid(), ClientId = _clientId, Title = "T", Client = new Client { Id = _clientId, Name = "C" } };
        _repo.Setup(r => r.GetDocumentAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        await Sut().SetDocumentActiveAsync(doc.Id, true, default);
        _knowledge.Verify(k => k.SetActiveAsync(_clientId, doc.Id, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("C", (await Sut().GetDocumentAsync(doc.Id, default))!.Client!.Name);
        Assert.Null(await Sut().GetDocumentAsync(Guid.NewGuid(), default));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().UpdateSearchSettingsAsync(_clientId, 4, 0.4, default));
        _repo.Setup(r => r.SearchSettingsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSearchSettings { Id = Guid.NewGuid(), ClientId = _clientId, ChunkSizeTokens = 300, ChunkOverlapTokens = 30 });
        await Sut().UpdateSearchSettingsAsync(_clientId, 4, 0.4, default);
        _settings.Verify(s => s.UpdateAsync(_clientId, new UpdateKnowledgeSettingsRequest(300, 30, 4, 0.4), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(300, (await Sut().SearchSettingsAsync(_clientId, default))!.ChunkSizeTokens);
        Assert.Null(await Sut().SearchSettingsAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Listings_delegate_and_overview_clamps()
    {
        await Sut().ListCampaignsAsync(_clientId, "draft", 1, default);
        await Sut().GetCampaignAsync(Guid.NewGuid(), default);
        await Sut().RecipientsAsync(Guid.NewGuid(), null, 1, default);
        await Sut().ListTemplatesAsync(_clientId, null, default);
        await Sut().ListServicesAsync(_clientId, default);
        await Sut().ListDocumentsAsync(_clientId, "q", true, 1, default);
        await Sut().ListWebsitesAsync(_clientId, default);
        _repo.Verify(r => r.ListCampaignsAsync(_clientId, "draft", 1, It.IsAny<CancellationToken>()), Times.Once);

        var overview = new Mock<IOverviewAdminRepository>();
        var sut = new OverviewAdminService(overview.Object);
        await sut.TotalsAsync(default);
        await sut.DailyMessagesAsync(1000, default);
        await sut.DailyMessagesAsync(0, default);
        await sut.RecentClientsAsync(500, default);
        await sut.RecentClientsAsync(-1, default);
        await sut.ProblemsAsync(default);
        await sut.EventsAsync(null, "x", 1, default);
        await sut.EventTypesAsync(default);
        overview.Verify(o => o.DailyMessagesAsync(90, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(o => o.DailyMessagesAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(o => o.RecentClientsAsync(100, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(o => o.RecentClientsAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class AutomationCleanupServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<ICacheManager> _cache = new();

    public AutomationCleanupServiceTests() => _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);

    private AutomationCleanupService Sut() => new(_clients.Object, _users.Object, _uow.Object, _cache.Object);

    private void Setup(string clientName, params string[] emails)
    {
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = clientName });
        _clients.Setup(c => c.ListMemberUserIdsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "u1" });
        _users.Setup(u => u.ListEmailsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(emails);
    }

    [Fact]
    public async Task Only_clients_that_are_clearly_automation_data_can_be_deleted()
    {
        Assert.Equal(AutomationDeleteOutcome.NotFound, await Sut().DeleteClientAsync(Guid.NewGuid()));

        Setup("[Automation] run 1", "bot@omni-automation.test");
        Assert.Equal(AutomationDeleteOutcome.Deleted, await Sut().DeleteClientAsync(_clientId));
        _clients.Verify(c => c.DeleteAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.DeleteAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(CacheKeys.Entitlements(_clientId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Real Client", "bot@omni-automation.test")]
    [InlineData("[Automation] run", "bot@omni-automation.test", "doctor@real.com")]
    [InlineData("[Automation] run")]
    [InlineData("automation run", "bot@omni-automation.test")]
    public async Task Anything_else_is_refused_and_nothing_is_deleted(string name, params string[] emails)
    {
        Setup(name, emails);
        Assert.Equal(AutomationDeleteOutcome.NotAutomationClient, await Sut().DeleteClientAsync(_clientId));
        _clients.Verify(c => c.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(u => u.DeleteAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listing_uses_the_automation_prefix()
    {
        await Sut().ListClientsAsync();
        _clients.Verify(c => c.ListByNamePrefixAsync(AutomationClients.NamePrefix, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class PlatformAdminMapperTests
{
    [Fact]
    public void Mappers_copy_fields_and_never_expose_the_access_token()
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "C", Slug = "c", Timezone = "UTC" };
        var channel = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = client.Id, Client = client, Channel = "whatsapp", AccessToken = "secret" };
        var detail = PlatformAdminMapper.ToDetail(channel);
        Assert.Equal((true, "C"), (detail.HasAccessToken, detail.Client!.Name));
        Assert.False(PlatformAdminMapper.ToDetail(new ChannelIntegration { Channel = "whatsapp" }).HasAccessToken);

        var ev = new WhatsAppHealthEvent { Id = Guid.NewGuid(), EventType = "x", Severity = "info" };
        Assert.Equal("x", PlatformAdminMapper.ToRow(ev).EventType);
        var log = new EventLog { Id = Guid.NewGuid(), EventType = "e", Metadata = "{}" };
        Assert.Equal("e", PlatformAdminMapper.ToRow(log).EventType);
        Assert.Equal("c", PlatformAdminMapper.ToDetail(client).Slug);
        var settings = new KnowledgeSearchSettings { Id = Guid.NewGuid(), TopK = 4 };
        Assert.Equal(4, PlatformAdminMapper.ToDetail(settings).TopK);
        var msg = new Message { Id = Guid.NewGuid(), Content = "hi", Direction = "inbound" };
        Assert.Equal("hi", PlatformAdminMapper.ToDetail(msg).Content);
        var convo = new Conversation { Id = Guid.NewGuid(), Channel = "whatsapp" };
        Assert.Null(PlatformAdminMapper.ToDetail(convo, DateTimeOffset.UtcNow).Contact);
    }
}
