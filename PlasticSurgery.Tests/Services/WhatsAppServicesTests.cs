using Microsoft.Extensions.Configuration;
using PlasticSurgery.Business.Services.WhatsApp;

namespace PlasticSurgery.Tests.Services;

public class WhatsAppTemplateServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IWhatsAppTemplateProvider> _provider = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private IConfiguration _configuration = new ConfigurationBuilder().Build();
    private ChannelIntegration? _integration;

    public WhatsAppTemplateServiceTests()
    {
        _provider.SetupGet(p => p.Name).Returns(ChannelProvider.Meta);
        _provider.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(true);
        _integrations.Setup(i => i.GetAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(() => _integration);
        Connect();
    }

    private void Connect(string provider = ChannelProvider.Meta) => _integration = new ChannelIntegration
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, Provider = provider, WhatsAppBusinessId = "W1"
    };

    private WhatsAppTemplateService Sut() => new(_templates.Object, _integrations.Object, _uow.Object, [_provider.Object], _configuration, _notifier.Object, _events.Object);

    private CreateWhatsAppTemplateRequest Create(string category = "marketing", string name = "promo", string body = "Hi", string language = "") =>
        new(_clinicId, name, category, language, body, null, null, null, null, null);

    [Theory]
    [InlineData("bogus", "n", "b")]
    [InlineData("marketing", " ", "b")]
    [InlineData("marketing", "n", " ")]
    public async Task Create_validates(string category, string name, string body) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create(category, name, body)));

    [Fact]
    public async Task Create_submits_to_the_provider_and_maps_the_status()
    {
        WhatsAppTemplate? added = null;
        _templates.Setup(t => t.Add(It.IsAny<WhatsAppTemplate>())).Callback<WhatsAppTemplate>(t => added = t);
        _provider.Setup(p => p.SubmitAsync(_integration!, It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("meta-1", "PENDING"));
        var r = await Sut().CreateAsync(Create());
        Assert.Equal(("en_US", "meta-1", WhatsAppTemplateStatus.Pending, null), (added!.Language, added.MetaTemplateId, added.Status, added.Provider));
        Assert.Equal(WhatsAppTemplateStatus.Pending, r.Status);
    }

    [Fact]
    public async Task Create_stamps_non_meta_providers_and_stays_draft_when_nothing_is_connected()
    {
        _provider.SetupGet(p => p.Name).Returns(ChannelProvider.Infobip);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = "infobip" }).Build();
        Connect(ChannelProvider.Infobip);
        WhatsAppTemplate? added = null;
        _templates.Setup(t => t.Add(It.IsAny<WhatsAppTemplate>())).Callback<WhatsAppTemplate>(t => added = t);
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("ib-1", "approved"));
        await Sut().CreateAsync(Create());
        Assert.Equal(("infobip", WhatsAppTemplateStatus.Approved), (added!.Provider, added.Status));

        _integration = null;
        var draft = await Sut().CreateAsync(Create(name: "other"));
        Assert.Equal(WhatsAppTemplateStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task A_provider_rejection_is_stored_not_thrown()
    {
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ThrowsAsync(new WhatsAppTemplateProviderException("Body too long"));
        var r = await Sut().CreateAsync(Create());
        Assert.Equal((WhatsAppTemplateStatus.Rejected, "Body too long"), (r.Status, r.RejectionReason));
    }

    [Fact]
    public async Task Unknown_provider_name_makes_templates_unavailable()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = "twilio" }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().CreateAsync(Create()));
    }

    private WhatsAppTemplate Existing(string? metaId = null, string? provider = null, string status = WhatsAppTemplateStatus.Draft)
    {
        var t = new WhatsAppTemplate { Id = Guid.NewGuid(), ClinicId = _clinicId, Name = "promo", Body = "b", MetaTemplateId = metaId, Provider = provider, Status = status };
        _templates.Setup(r => r.GetAsync(_clinicId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        return t;
    }

    [Fact]
    public async Task Retry_submit_rules()
    {
        Assert.Null(await Sut().RetrySubmitAsync(_clinicId, Guid.NewGuid()));
        var sent = Existing("meta-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RetrySubmitAsync(_clinicId, sent.Id));

        var draft = Existing();
        _integration = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RetrySubmitAsync(_clinicId, draft.Id));

        Connect();
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), draft, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("m2", "APPROVED"));
        var r = await Sut().RetrySubmitAsync(_clinicId, draft.Id);
        Assert.Equal(WhatsAppTemplateStatus.Approved, r!.Status);
    }

    [Fact]
    public async Task Sync_refreshes_the_status_and_keeps_or_clears_the_reason()
    {
        Assert.Null(await Sut().SyncStatusAsync(_clinicId, Guid.NewGuid()));
        var unsent = Existing();
        Assert.Equal(WhatsAppTemplateStatus.Draft, (await Sut().SyncStatusAsync(_clinicId, unsent.Id))!.Status);
        _provider.Verify(p => p.GetStatusAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>()), Times.Never);

        var t = Existing("meta-1", status: WhatsAppTemplateStatus.Pending);
        _provider.Setup(p => p.GetStatusAsync(It.IsAny<ChannelIntegration>(), t, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateStatusResult("REJECTED", "ABUSIVE"));
        var rejected = await Sut().SyncStatusAsync(_clinicId, t.Id);
        Assert.Equal((WhatsAppTemplateStatus.Rejected, "ABUSIVE"), (rejected!.Status, rejected.RejectionReason));

        _provider.Setup(p => p.GetStatusAsync(It.IsAny<ChannelIntegration>(), t, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateStatusResult("APPROVED", null));
        var approved = await Sut().SyncStatusAsync(_clinicId, t.Id);
        Assert.Null(approved!.RejectionReason);
    }

    [Fact]
    public async Task Sync_refuses_templates_from_another_provider_or_without_a_connection()
    {
        var other = Existing("meta-1", provider: "infobip");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SyncStatusAsync(_clinicId, other.Id));
        var mine = Existing("meta-2");
        _integration = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SyncStatusAsync(_clinicId, mine.Id));
    }

    [Fact]
    public async Task List_and_GetById_map_templates()
    {
        var t = Existing();
        _templates.Setup(r => r.ListForClinicAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppTemplate> { t });
        Assert.Single(await Sut().ListAsync(_clinicId));
        Assert.Equal(t.Id, (await Sut().GetByIdAsync(_clinicId, t.Id))!.Id);
        Assert.Null(await Sut().GetByIdAsync(_clinicId, Guid.NewGuid()));
    }

    private WhatsAppTemplateEventRequest Event(string? waba = "W1", string? metaId = "meta-1", string? status = "APPROVED", string? category = null) =>
        new(_clinicId, "template_status_update", waba, metaId, "promo", null, category, status, null, null, null, null, null, null);

    [Fact]
    public async Task Meta_event_updates_an_existing_template_by_provider_id_and_notifies()
    {
        var t = Existing("meta-1", status: WhatsAppTemplateStatus.Pending);
        _templates.Setup(r => r.FindByProviderTemplateIdAsync(_clinicId, "meta-1", It.IsAny<CancellationToken>())).ReturnsAsync(t);
        var r = await Sut().ApplyMetaEventAsync(Event() with { Reason = "NONE", QualityRating = "GREEN", CurrentCategory = "utility", Category = "UTILITY" });
        Assert.Equal((WhatsAppTemplateStatus.Approved, "NONE", "GREEN", "utility", "utility"), (t.Status, t.RejectionReason, t.QualityRating, t.CurrentCategory, t.Category));
        Assert.Equal(_integration!.Id, t.ChannelIntegrationId);
        _notifier.Verify(n => n.WhatsAppTemplateUpdatedAsync(_clinicId, t.Id, "promo", WhatsAppTemplateStatus.Approved, "NONE", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clinicId, EventTypes.WhatsAppTemplateStatusChanged, null, null, null, "n8n", It.Is<string>(s => s.Contains("\"previousStatus\":\"pending\""))), Times.Once);
        Assert.Equal(t.Id, r.Id);
    }

    [Fact]
    public async Task Meta_event_falls_back_to_name_and_language_then_creates_an_unknown_template()
    {
        var byName = Existing();
        _templates.Setup(r => r.FindByNameAsync(_clinicId, "promo", "en_US", It.IsAny<CancellationToken>())).ReturnsAsync(byName);
        await Sut().ApplyMetaEventAsync(Event(metaId: null, status: "PAUSED"));
        Assert.Equal(WhatsAppTemplateStatus.Paused, byName.Status);

        WhatsAppTemplate? added = null;
        _templates.Setup(r => r.Add(It.IsAny<WhatsAppTemplate>())).Callback<WhatsAppTemplate>(t => added = t);
        _templates.Setup(r => r.FindByNameAsync(_clinicId, "promo", "en_US", It.IsAny<CancellationToken>())).ReturnsAsync((WhatsAppTemplate?)null);
        await Sut().ApplyMetaEventAsync(Event(status: "WEIRD", category: "MARKETING"));
        Assert.Equal(("meta-1", "weird", "marketing", ""), (added!.MetaTemplateId, added.Status, added.Category, added.Body));
    }

    [Fact]
    public async Task Meta_event_validates_name_and_waba_ownership()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ApplyMetaEventAsync(Event() with { Name = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ApplyMetaEventAsync(Event(waba: "OTHER")));
        await Sut().ApplyMetaEventAsync(Event(waba: "w1")); // case-insensitive match is fine
    }

    [Theory]
    [InlineData("pending", WhatsAppTemplateStatus.Pending)]
    [InlineData("APPROVED", WhatsAppTemplateStatus.Approved)]
    [InlineData("Rejected", WhatsAppTemplateStatus.Rejected)]
    [InlineData("PAUSED", WhatsAppTemplateStatus.Paused)]
    [InlineData("DISABLED", WhatsAppTemplateStatus.Disabled)]
    [InlineData("FLAGGED", WhatsAppTemplateStatus.Flagged)]
    [InlineData("DELETED", WhatsAppTemplateStatus.Deleted)]
    [InlineData("IN_APPEAL", "in_appeal")]
    public void Status_mapping(string meta, string expected) => Assert.Equal(expected, WhatsAppTemplateService.MapMetaStatus(meta));

    [Fact]
    public void Template_provider_defaults_to_meta()
    {
        Assert.Equal(ChannelProvider.Meta, WhatsAppTemplateService.TemplateProviderOf(new WhatsAppTemplate { Provider = " " }));
        Assert.Equal("infobip", WhatsAppTemplateService.TemplateProviderOf(new WhatsAppTemplate { Provider = "infobip" }));
    }
}

public class WhatsAppHealthServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IWhatsAppHealthEventRepository> _healthEvents = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly ChannelIntegration _integration;

    public WhatsAppHealthServiceTests()
    {
        _integration = new ChannelIntegration
        {
            Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected,
            WhatsAppBusinessId = "W1", PhoneNumberId = "P1", HealthLevel = WhatsAppHealthLevel.Healthy
        };
        _integrations.Setup(i => i.GetAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(_integration);
    }

    private WhatsAppHealthService Sut() => new(_integrations.Object, _healthEvents.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object);

    private WhatsAppHealthEventRequest Req(string type, string? status = null, string? quality = null, string? waba = "W1", string? phone = "P1", string? code = null, string? message = null, DateTimeOffset? at = null) =>
        new(_clinicId, type, waba, phone, status, quality, code, message, at, "{}");

    [Fact]
    public async Task Health_is_null_without_a_connection_and_unknown_until_an_event_arrives()
    {
        Assert.Null(await new WhatsAppHealthService(Mock.Of<IChannelIntegrationRepository>(), _healthEvents.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object).GetHealthAsync(_clinicId));
        _integration.HealthLevel = null;
        Assert.Equal(WhatsAppHealthLevel.Unknown, (await Sut().GetHealthAsync(_clinicId))!.HealthLevel);
    }

    [Fact]
    public async Task Event_list_is_mapped()
    {
        _healthEvents.Setup(h => h.ListForClinicAsync(_clinicId, 0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEvent> { new() { Id = Guid.NewGuid(), EventType = "x", Severity = "info" } });
        Assert.Single(await Sut().GetHealthEventsAsync(_clinicId, 0, 10));
    }

    [Fact]
    public async Task Events_need_a_connection_and_must_match_the_stored_waba_and_phone()
    {
        var none = new WhatsAppHealthService(Mock.Of<IChannelIntegrationRepository>(), _healthEvents.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, waba: "OTHER")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, phone: "OTHER")));
        _healthEvents.Verify(h => h.Add(It.IsAny<WhatsAppHealthEvent>()), Times.Never);
    }

    [Theory]
    [InlineData(WhatsAppHealthEventType.AccountUpdate, "DISABLED", null, WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.AccountUpdate, "banned", null, WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.AccountUpdate, "VERIFIED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.AccountReviewUpdate, "REJECTED", null, WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.AccountReviewUpdate, "APPROVED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, null, "RED", WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, null, "YELLOW", WhatsAppHealthLevel.Warning)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "FLAGGED", "GREEN", WhatsAppHealthLevel.Warning)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "CONNECTED", "GREEN", WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberNameUpdate, "APPROVED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.ConnectionUpdate, null, null, WhatsAppHealthLevel.Healthy)]
    [InlineData("something_else", "X", "RED", WhatsAppHealthLevel.Healthy)]
    public async Task Health_level_follows_the_event(string type, string? status, string? quality, string expectedLevel)
    {
        var r = await Sut().ApplyHealthEventAsync(Req(type, status, quality, message: "m", code: "c"));
        Assert.Equal(expectedLevel, r.HealthLevel);
        _healthEvents.Verify(h => h.Add(It.Is<WhatsAppHealthEvent>(e => e.EventType == type && e.ClinicId == _clinicId)), Times.Once);
        _notifier.Verify(n => n.WhatsAppHealthUpdatedAsync(_clinicId, _integration.Id, expectedLevel, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Severity_matches_the_level()
    {
        WhatsAppHealthEvent? stored = null;
        _healthEvents.Setup(h => h.Add(It.IsAny<WhatsAppHealthEvent>())).Callback<WhatsAppHealthEvent>(e => stored = e);
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED"));
        Assert.Equal(WhatsAppHealthEventSeverity.Error, stored!.Severity);
        _integration.AccountStatus = null;
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberQualityUpdate, quality: "YELLOW"));
        Assert.Equal(WhatsAppHealthEventSeverity.Warning, stored.Severity);
        _integration.Status = ChannelIntegrationStatus.Disconnected;
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "VERIFIED"));
        Assert.Equal(WhatsAppHealthEventSeverity.Critical, stored.Severity);
        _integration.Status = ChannelIntegrationStatus.Connected;
        _integration.AccountStatus = null;
        _integration.PhoneQualityRating = null;
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberNameUpdate, "APPROVED"));
        Assert.Equal(WhatsAppHealthEventSeverity.Info, stored.Severity);
    }

    [Fact]
    public async Task Staff_are_notified_only_when_the_connection_turns_unhealthy()
    {
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED", message: "Account disabled by Meta"));
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.IntegrationUnhealthy, "WhatsApp connection needs attention", It.IsAny<string?>(), null, null, null, _integration.Id, "/WhatsApp/Health", It.IsAny<CancellationToken>()), Times.Once);

        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED")); // already unhealthy
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_repeated_event_is_ignored()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-3);
        _healthEvents.Setup(h => h.ExistsAsync(_integration.Id, WhatsAppHealthEventType.AccountUpdate, at, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED", at: at));
        _healthEvents.Verify(h => h.Add(It.IsAny<WhatsAppHealthEvent>()), Times.Never);
        Assert.Equal(WhatsAppHealthLevel.Healthy, _integration.HealthLevel);
    }

    [Fact]
    public async Task Connection_updates_record_the_error_message()
    {
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.ConnectionUpdate, message: "lost"));
        Assert.Equal("lost", _integration.LastError);
    }
}
