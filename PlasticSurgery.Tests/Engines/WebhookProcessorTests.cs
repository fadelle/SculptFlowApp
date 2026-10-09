using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Engines.Infobip;
using PlasticSurgery.Business.Engines.Telegram;
using PlasticSurgery.Business.Engines.WhatsApp;
using PlasticSurgery.Business.Engines.WhatsApp.Handlers;

namespace PlasticSurgery.Tests.Engines;

/// <summary>Shared fakes for the webhook handler/processor tests.</summary>
public static class ParsedEventExtensions { public static ParsedMetaEvent With(this ParsedMetaEvent e, params (string Name, object? Value)[] changes) { foreach (var (n, v) in changes) typeof(ParsedMetaEvent).GetProperty(n)!.SetValue(e, v); return e; } }

public abstract class WebhookTestBase
{
    protected readonly Guid ClinicId = Guid.NewGuid();
    protected readonly Guid ConnectionId = Guid.NewGuid();
    protected readonly Guid LeadId = Guid.NewGuid();
    protected readonly Guid ConversationId = Guid.NewGuid();
    protected readonly Mock<ILeadService> Leads = new();
    protected readonly Mock<IConversationService> Conversations = new();
    protected readonly Mock<IMessageService> Messages = new();
    protected readonly Mock<IUnitOfWork> Uow = new();
    protected readonly Mock<IEventLogger> Events = new();
    protected readonly Mock<IWhatsAppHealthService> Health = new();
    protected readonly Mock<IWhatsAppTemplateService> Templates = new();
    protected readonly Mock<IChannelIntegrationRepository> Integrations = new();
    protected readonly Mock<IAiTriggerNotifier> AiTrigger = new();
    protected readonly ResolvedClinic Clinic;

    protected WebhookTestBase()
    {
        Clinic = new ResolvedClinic(ClinicId, ConnectionId);
        Leads.Setup(l => l.GetOrCreateByPhoneAsync(ClinicId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Lead(), true));
        Conversations.Setup(c => c.GetOrCreateForLeadAsync(ClinicId, LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Conv());
        Conversations.Setup(c => c.GetOrCreateForLeadAsync(ClinicId, LeadId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Conv());
        Messages.Setup(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestMessageResult(true, false, Msg(), ConversationMode.Ai, true));
    }

    protected LeadResponse Lead() => new(LeadId, ClinicId, null, null, "Ann", null, null, "+961", null, "whatsapp", null, null, null, "new", "unknown", null, null, null, null, true, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    protected ConversationResponse Conv() => new(ConversationId, ClinicId, LeadId, "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);

    protected MessageResponse Msg() => new(Guid.NewGuid(), ConversationId, LeadId, "inbound", "lead", "whatsapp_customer", "whatsapp", "text", "hello", null, false, null, null, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null);

    protected static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    protected static ParsedMetaEvent Evt(ParsedMetaEventKind kind, string? waId = "96170123456", string? name = "Ann") => new()
    {
        Kind = kind, CustomerWaId = waId, CustomerName = name, Content = "hello", MessageType = "text", ExternalMessageId = "wamid.1", RawJson = "{}", WabaId = "W1", PhoneNumberId = "P1"
    };
}

public class WhatsAppHandlerTests : WebhookTestBase
{
    [Fact]
    public async Task CustomerMessage_creates_lead_conversation_and_ingests_with_a_plus_prefixed_phone()
    {
        var handler = new CustomerMessageHandler(Leads.Object, Conversations.Object, Messages.Object);
        var r = await handler.HandleAsync(Evt(ParsedMetaEventKind.CustomerMessage).With(("SelectedValue", "btn1")), Clinic, default);

        Leads.Verify(l => l.GetOrCreateByPhoneAsync(ClinicId, "+96170123456", "Ann", It.IsAny<CancellationToken>()), Times.Once);
        Messages.Verify(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.EventType == IngestEventType.CustomerMessage && i.LeadWasNewlyCreated && i.ExternalMessageId == "wamid.1" && i.Content == "hello"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(("customer_message", true, ConversationId, "btn1", "hello"), (r.EventType, r.ShouldRunAi, r.ConversationId, r.SelectedValue, r.Content));
    }

    [Fact]
    public async Task CustomerMessage_without_a_sender_is_acknowledged_without_side_effects()
    {
        var handler = new CustomerMessageHandler(Leads.Object, Conversations.Object, Messages.Object);
        var r = await handler.HandleAsync(Evt(ParsedMetaEventKind.CustomerMessage, waId: " "), Clinic, default);
        Assert.True(r.Processed);
        Assert.False(r.ShouldRunAi);
        Messages.Verify(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Existing_plus_in_the_sender_id_is_not_doubled()
    {
        var handler = new CustomerMessageHandler(Leads.Object, Conversations.Object, Messages.Object);
        await handler.HandleAsync(Evt(ParsedMetaEventKind.CustomerMessage, waId: "+96170"), Clinic, default);
        Leads.Verify(l => l.GetOrCreateByPhoneAsync(ClinicId, "+96170", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BusinessAppEcho_is_ingested_as_an_echo_and_never_triggers_ai()
    {
        var handler = new BusinessAppEchoHandler(Leads.Object, Conversations.Object, Messages.Object);
        var r = await handler.HandleAsync(Evt(ParsedMetaEventKind.BusinessAppEcho), Clinic, default);
        Messages.Verify(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.EventType == IngestEventType.BusinessAppEcho), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(("business_app_echo", false), (r.EventType, r.ShouldRunAi));
        var none = await handler.HandleAsync(Evt(ParsedMetaEventKind.BusinessAppEcho, waId: null), Clinic, default);
        Assert.Null(none.ConversationId);
    }

    [Fact]
    public async Task MessageStatus_reports_processed_only_when_the_message_was_found()
    {
        var handler = new MessageStatusHandler(Messages.Object);
        Messages.Setup(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.EventType == IngestEventType.StatusUpdate), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestMessageResult(false, false, null));
        var missing = await handler.HandleAsync(Evt(ParsedMetaEventKind.MessageStatus).With(("DeliveryStatus", "read")), Clinic, default);
        Assert.False(missing.Processed);

        Messages.Setup(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.EventType == IngestEventType.StatusUpdate), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestMessageResult(true, false, Msg()));
        var found = await handler.HandleAsync(Evt(ParsedMetaEventKind.MessageStatus).With(("DeliveryStatus", "read"), ("FailureCode", "1")), Clinic, default);
        Assert.Equal((true, "message_status", "read", ConversationId), (found.Processed, found.EventType, found.Status, found.ConversationId));
        Messages.Verify(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.FailureCode == "1" && i.ExternalMessageId == "wamid.1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Template_event_is_forwarded_with_defaults()
    {
        var tpl = new WhatsAppTemplateResponse(Guid.NewGuid(), ClinicId, "m1", "promo", "MARKETING", "en", "approved", null, null, "b", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Templates.Setup(t => t.ApplyMetaEventAsync(It.IsAny<WhatsAppTemplateEventRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(tpl);
        var r = await new TemplateEventHandler(Templates.Object).HandleAsync(Evt(ParsedMetaEventKind.TemplateStatus).With(("RawFieldName", null), ("TemplateName", null), ("TemplateStatus", "APPROVED")), Clinic, default);
        Templates.Verify(t => t.ApplyMetaEventAsync(It.Is<WhatsAppTemplateEventRequest>(q => q.EventType == "template_status_update" && q.Name == "unknown" && q.Status == "APPROVED"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(("template_status", tpl.Id, "approved"), (r.EventType, r.TemplateId, r.Status));
    }

    [Fact]
    public async Task Health_event_is_applied_and_the_level_returned()
    {
        Health.Setup(h => h.ApplyHealthEventAsync(It.IsAny<WhatsAppHealthEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppHealthResponse(ConnectionId, ClinicId, true, "warning", null, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow));
        var r = await new HealthEventHandler(Health.Object).HandleAsync(Evt(ParsedMetaEventKind.WhatsAppHealth).With(("HealthEventType", null), ("HealthStatus", "FLAGGED")), Clinic, default);
        Health.Verify(h => h.ApplyHealthEventAsync(It.Is<WhatsAppHealthEventRequest>(q => q.EventType == WhatsAppHealthEventType.UnknownMetaHealthEvent && q.Status == "FLAGGED"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(("whatsapp_health", "warning"), (r.EventType, r.HealthLevel));
    }

    [Fact]
    public async Task History_app_state_and_unknown_events_are_only_logged()
    {
        var history = await new HistoryHandler(Uow.Object, Events.Object).HandleAsync(Evt(ParsedMetaEventKind.HistorySync), Clinic, default);
        var state = await new AppStateSyncHandler(Uow.Object, Events.Object).HandleAsync(Evt(ParsedMetaEventKind.AppStateSync), Clinic, default);
        var unknown = await new UnknownEventHandler(Uow.Object, Events.Object).HandleAsync(Evt(ParsedMetaEventKind.Unknown), Clinic, default);
        Assert.Equal(["history", "app_state_sync", "unknown"], [history.EventType, state.EventType, unknown.EventType]);
        Assert.All(new[] { history, state, unknown }, r => Assert.False(r.ShouldRunAi));
        Events.Verify(e => e.Log(ClinicId, EventTypes.WhatsAppHistorySyncReceived, null, null, null, "n8n", It.IsAny<string>()), Times.Once);
        Events.Verify(e => e.Log(ClinicId, EventTypes.WhatsAppAppStateSyncReceived, null, null, null, "n8n", It.IsAny<string>()), Times.Once);
        Events.Verify(e => e.Log(ClinicId, EventTypes.WhatsAppUnknownEventReceived, null, null, null, "n8n", It.IsAny<string>()), Times.Once);

        var orphan = await new UnknownEventHandler(Uow.Object, Events.Object).HandleAsync(Evt(ParsedMetaEventKind.Unknown), null, default);
        Assert.Null(orphan.ClinicId);
        Events.Verify(e => e.Log(It.IsAny<Guid>(), EventTypes.WhatsAppUnknownEventReceived, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Once);
    }
}

public class MetaWebhookProcessorTests : WebhookTestBase
{
    private readonly Mock<ICacheManager> _cache = new();

    public MetaWebhookProcessorTests()
    {
        _cache.Setup(c => c.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<Func<CancellationToken, Task<ResolvedClinic?>>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, TimeSpan _, Func<CancellationToken, Task<ResolvedClinic?>> f, CancellationToken ct) => f(ct));
        Integrations.Setup(i => i.FindWhatsAppByPhoneNumberIdAsync("P1", It.IsAny<CancellationToken>())).ReturnsAsync(new ChannelIntegration { Id = ConnectionId, ClinicId = ClinicId });
        Integrations.Setup(i => i.FindWhatsAppByWabaIdAsync("W1", It.IsAny<CancellationToken>())).ReturnsAsync(new ChannelIntegration { Id = ConnectionId, ClinicId = ClinicId });
    }

    private MetaWebhookProcessor Sut() => new(Integrations.Object, _cache.Object,
        new CustomerMessageHandler(Leads.Object, Conversations.Object, Messages.Object),
        new BusinessAppEchoHandler(Leads.Object, Conversations.Object, Messages.Object),
        new MessageStatusHandler(Messages.Object),
        new TemplateEventHandler(Templates.Object),
        new HealthEventHandler(Health.Object),
        new HistoryHandler(Uow.Object, Events.Object),
        new AppStateSyncHandler(Uow.Object, Events.Object),
        new UnknownEventHandler(Uow.Object, Events.Object));

    private static string Body(string phoneNumberId = "P1", string waId = "961700", string messageId = "m1") =>
        "{\"entry\":[{\"id\":\"W1\",\"changes\":[{\"field\":\"messages\",\"value\":{\"metadata\":{\"phone_number_id\":\"" + phoneNumberId + "\"},"
        + "\"contacts\":[{\"wa_id\":\"" + waId + "\",\"profile\":{\"name\":\"Ann\"}}],\"messages\":[{\"from\":\"" + waId + "\",\"id\":\"" + messageId + "\",\"type\":\"text\",\"text\":{\"body\":\"hi\"}}]}}]}]}";

    [Fact]
    public async Task Empty_payload_is_acknowledged_as_unknown()
    {
        var r = await Sut().ProcessAsync(J("{}"));
        Assert.Equal(("unknown", true, false), (r.EventType, r.Processed, r.ShouldRunAi));
    }

    [Fact]
    public async Task Customer_message_is_routed_by_phone_number_id()
    {
        var r = await Sut().ProcessAsync(J(Body()));
        Assert.Equal(("customer_message", true, ClinicId), (r.EventType, r.ShouldRunAi, r.ClinicId));
    }

    [Fact]
    public async Task Unrouteable_numbers_fall_back_to_waba_then_to_unknown()
    {
        var viaWaba = await Sut().ProcessAsync(J(Body("P-unknown")));
        Assert.Equal("customer_message", viaWaba.EventType);

        Integrations.Setup(i => i.FindWhatsAppByWabaIdAsync("W1", It.IsAny<CancellationToken>())).ReturnsAsync((ChannelIntegration?)null);
        var unknown = await Sut().ProcessAsync(J(Body("P-unknown")));
        Assert.Equal(("unknown", null), (unknown.EventType, unknown.ClinicId));
        Messages.Verify(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Each_event_kind_goes_to_its_own_handler()
    {
        Templates.Setup(t => t.ApplyMetaEventAsync(It.IsAny<WhatsAppTemplateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppTemplateResponse(Guid.NewGuid(), ClinicId, "m", "n", "c", "en", "approved", null, null, "b", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Health.Setup(h => h.ApplyHealthEventAsync(It.IsAny<WhatsAppHealthEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppHealthResponse(ConnectionId, ClinicId, true, "healthy", null, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow));
        Messages.Setup(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.EventType == IngestEventType.StatusUpdate), It.IsAny<CancellationToken>())).ReturnsAsync(new IngestMessageResult(true, false, Msg()));

        string Wrap(string field, string value) => "{\"entry\":[{\"id\":\"W1\",\"changes\":[{\"field\":\"" + field + "\",\"value\":" + value + "}]}]}";
        const string meta = "\"metadata\":{\"phone_number_id\":\"P1\"}";

        Assert.Equal("business_app_echo", (await Sut().ProcessAsync(J(Wrap("smb_message_echoes", "{" + meta + ",\"messages\":[{\"from\":\"961\",\"id\":\"e\",\"type\":\"text\",\"text\":{\"body\":\"x\"}}]}")))).EventType);
        Assert.Equal("message_status", (await Sut().ProcessAsync(J(Wrap("messages", "{" + meta + ",\"statuses\":[{\"id\":\"w\",\"status\":\"read\"}]}")))).EventType);
        Assert.Equal("template_status", (await Sut().ProcessAsync(J(Wrap("message_template_status_update", "{\"message_template_name\":\"n\",\"event\":\"APPROVED\"}")))).EventType);
        Assert.Equal("whatsapp_health", (await Sut().ProcessAsync(J(Wrap("phone_number_quality_update", "{\"phone_number_id\":\"P1\",\"event\":\"FLAGGED\"}")))).EventType);
        Assert.Equal("history", (await Sut().ProcessAsync(J(Wrap("history", "{" + meta + "}")))).EventType);
        Assert.Equal("app_state_sync", (await Sut().ProcessAsync(J(Wrap("smb_app_state_sync", "{}")))).EventType);
        Assert.Equal("unknown", (await Sut().ProcessAsync(J(Wrap("brand_new", "{}")))).EventType);
    }

    [Fact]
    public async Task Every_ai_eligible_message_in_a_batch_triggers_the_ai()
    {
        var second = "{\"entry\":[{\"id\":\"W1\",\"changes\":["
            + "{\"field\":\"messages\",\"value\":{\"metadata\":{\"phone_number_id\":\"P1\"},\"messages\":[{\"from\":\"961700\",\"id\":\"a\",\"type\":\"text\",\"text\":{\"body\":\"hi\"}}]}},"
            + "{\"field\":\"messages\",\"value\":{\"metadata\":{\"phone_number_id\":\"P1\"},\"messages\":[{\"from\":\"961711\",\"id\":\"b\",\"type\":\"text\",\"text\":{\"body\":\"hello\"}}]}}]}]}";
        var other = Guid.NewGuid();
        var calls = 0;
        Conversations.Setup(c => c.GetOrCreateForLeadAsync(ClinicId, LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1 ? Conv() : Conv() with { Id = other });
        var all = await Sut().ProcessAllAsync(J(second));
        Assert.Equal(2, all.Count(r => r.ShouldRunAi));
        Assert.Contains(all, r => r.ConversationId == other);
    }
}

public class TelegramWebhookProcessorTests : WebhookTestBase
{
    private ChannelIntegration Connection(string status = ChannelIntegrationStatus.Connected, string channel = ChannelType.Telegram, string secret = "s3cret") => new()
    {
        Id = ConnectionId, ClinicId = ClinicId, Channel = channel, Status = status, WebhookVerifyToken = secret
    };

    private TelegramWebhookProcessor Sut() => new(Integrations.Object, AiTrigger.Object, Leads.Object, Conversations.Object, Messages.Object, NullLogger<TelegramWebhookProcessor>.Instance);

    private static JsonElement Update(string text = "hello") =>
        J("{\"update_id\":1,\"message\":{\"message_id\":7,\"date\":1700000000,\"chat\":{\"id\":42,\"type\":\"private\"},\"from\":{\"id\":42,\"is_bot\":false,\"first_name\":\"Ann\",\"username\":\"ann\"},\"text\":\"" + text + "\"}}");

    public TelegramWebhookProcessorTests()
    {
        Leads.Setup(l => l.GetOrCreateByExternalIdAsync(ClinicId, "telegram:42", "telegram", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Lead(), true));
    }

    [Fact]
    public async Task Customer_message_creates_lead_by_chat_id_conversation_and_message()
    {
        var r = await Sut().ProcessAsync(Connection(), Update());
        Assert.Equal(("customer_message", true, ConversationId), (r.EventType, r.ShouldRunAi, r.ConversationId));
        Leads.Verify(l => l.GetOrCreateByExternalIdAsync(ClinicId, "telegram:42", "telegram", "Ann", "Ann", null, "@ann", It.IsAny<CancellationToken>()), Times.Once);
        Conversations.Verify(c => c.GetOrCreateForLeadAsync(ClinicId, LeadId, "telegram", "42", It.IsAny<CancellationToken>()), Times.Once);
        Messages.Verify(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.ExternalMessageId == "42:7" && i.LeadWasNewlyCreated && i.Channel == "telegram"), It.IsAny<CancellationToken>()), Times.Once);
        Integrations.Verify(i => i.TouchLastWebhookAsync(ConnectionId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unsupported_updates_are_ignored_but_still_touch_the_connection()
    {
        var r = await Sut().ProcessAsync(Connection(), J("{\"update_id\":2,\"edited_message\":{}}"));
        Assert.Equal(("ignored", false, false), (r.EventType, r.Processed, r.ShouldRunAi));
        Integrations.Verify(i => i.TouchLastWebhookAsync(ConnectionId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        Messages.Verify(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_lost_duplicate_insert_race_is_reported_as_deduplicated()
    {
        Messages.Setup(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateRecordException(new Exception("dup")));
        var r = await Sut().ProcessAsync(Connection(), Update());
        Assert.Equal((true, true, false), (r.Processed, r.Deduplicated, r.ShouldRunAi));
    }

    [Fact]
    public async Task Names_fall_back_to_username_then_a_generated_label()
    {
        Leads.Setup(l => l.GetOrCreateByExternalIdAsync(ClinicId, It.IsAny<string>(), "telegram", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Lead(), false));
        await Sut().ProcessAsync(Connection(), J("{\"update_id\":1,\"message\":{\"message_id\":1,\"chat\":{\"id\":42,\"type\":\"private\",\"username\":\"bob\"},\"text\":\"x\"}}"));
        Leads.Verify(l => l.GetOrCreateByExternalIdAsync(ClinicId, "telegram:42", "telegram", "@bob", null, null, "@bob", It.IsAny<CancellationToken>()), Times.Once);
        await Sut().ProcessAsync(Connection(), J("{\"update_id\":1,\"message\":{\"message_id\":1,\"chat\":{\"id\":42,\"type\":\"private\"},\"text\":\"x\"}}"));
        Leads.Verify(l => l.GetOrCreateByExternalIdAsync(ClinicId, "telegram:42", "telegram", "Telegram user 42", null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receive_rejects_unknown_disconnected_wrong_channel_and_bad_secret()
    {
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAsync(ConnectionId, "s3cret", Update()));
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection(ChannelIntegrationStatus.Disconnected));
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAsync(ConnectionId, "s3cret", Update()));
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection(channel: ChannelType.WhatsApp));
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAsync(ConnectionId, "s3cret", Update()));
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection());
        Assert.Equal(WebhookReceiveOutcome.Forbidden, await Sut().ReceiveAsync(ConnectionId, "wrong", Update()));
        Assert.Equal(WebhookReceiveOutcome.Forbidden, await Sut().ReceiveAsync(ConnectionId, null, Update()));
        Messages.Verify(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Receive_accepts_and_triggers_the_ai_for_eligible_messages()
    {
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection());
        Assert.Equal(WebhookReceiveOutcome.Accepted, await Sut().ReceiveAsync(ConnectionId, "s3cret", Update()));
        AiTrigger.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == ConversationId && p.Channel == "telegram" && p.MessageText == "hello"), It.IsAny<CancellationToken>()), Times.Once);

        AiTrigger.Invocations.Clear();
        Messages.Setup(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IngestMessageResult(true, false, Msg(), ConversationMode.Human, false));
        await Sut().ReceiveAsync(ConnectionId, "s3cret", Update());
        AiTrigger.Verify(a => a.NotifyAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void External_message_ids_combine_chat_and_message() => Assert.Equal("42:7", TelegramWebhookProcessor.ExternalMessageId("42", 7));
}

public class InfobipWebhookProcessorTests : WebhookTestBase
{
    private readonly Mock<IWhatsAppTemplateRepository> _templatesRepo = new();
    private IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Infobip:WebhookToken"] = "acct-token" }).Build();

    private ChannelIntegration Connection(string sender = "447000", string status = ChannelIntegrationStatus.Connected, string token = "tok", string provider = ChannelProvider.Infobip) => new()
    {
        Id = ConnectionId, ClinicId = ClinicId, Channel = ChannelType.WhatsApp, Status = status, WebhookVerifyToken = token, ProviderSenderId = sender, Provider = provider
    };

    private InfobipWhatsAppWebhookProcessor Sut() => new(Integrations.Object, _templatesRepo.Object, AiTrigger.Object, _configuration,
        new CustomerMessageHandler(Leads.Object, Conversations.Object, Messages.Object),
        new MessageStatusHandler(Messages.Object),
        new TemplateEventHandler(Templates.Object),
        new UnknownEventHandler(Uow.Object, Events.Object),
        NullLogger<InfobipWhatsAppWebhookProcessor>.Instance);

    private static string Inbound(string to = "447000", string from = "96170123456", string id = "m1") =>
        "{\"results\":[{\"from\":\"" + from + "\",\"to\":\"" + to + "\",\"messageId\":\"" + id + "\",\"receivedAt\":\"2026-03-05T10:00:00.000+0000\",\"contact\":{\"name\":\"Zed\"},\"message\":{\"type\":\"TEXT\",\"text\":\"hello\"}}]}";

    [Fact]
    public async Task Inbound_message_for_this_sender_is_ingested()
    {
        var results = await Sut().ProcessAsync(Connection(), J(Inbound()));
        var r = Assert.Single(results);
        Assert.Equal(("customer_message", true), (r.EventType, r.ShouldRunAi));
        Integrations.Verify(i => i.TouchLastWebhookAsync(ConnectionId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Inbound_for_a_different_sender_is_logged_as_unknown_and_not_ingested()
    {
        var r = Assert.Single(await Sut().ProcessAsync(Connection(sender: "999"), J(Inbound())));
        Assert.Equal("unknown", r.EventType);
        Messages.Verify(m => m.IngestAsync(It.IsAny<IngestMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Events.Verify(e => e.Log(ClinicId, EventTypes.WhatsAppUnknownEventReceived, null, null, null, "n8n", It.Is<string>(s => s.Contains("sender_mismatch"))), Times.Once);
    }

    [Fact]
    public async Task Delivery_reports_go_to_the_status_handler_and_unknown_ones_are_logged()
    {
        Messages.Setup(m => m.IngestAsync(It.Is<IngestMessageRequest>(i => i.EventType == IngestEventType.StatusUpdate), It.IsAny<CancellationToken>())).ReturnsAsync(new IngestMessageResult(true, false, Msg()));
        var status = Assert.Single(await Sut().ProcessAsync(Connection(), J("{\"results\":[{\"messageId\":\"m\",\"status\":{\"groupName\":\"DELIVERED\"}}]}")));
        Assert.Equal("message_status", status.EventType);
        var unknown = Assert.Single(await Sut().ProcessAsync(Connection(), J("{\"results\":[{\"x\":1}]}")));
        Assert.Equal("unknown", unknown.EventType);
    }

    [Fact]
    public async Task Receive_checks_connection_provider_status_and_token()
    {
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAsync(ConnectionId, "tok", J(Inbound())));
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection(sender: "447000", status: ChannelIntegrationStatus.Connected, token: "tok", provider: ChannelProvider.Meta));
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAsync(ConnectionId, "tok", J(Inbound())));
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection(status: ChannelIntegrationStatus.Disconnected));
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAsync(ConnectionId, "tok", J(Inbound())));
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection());
        Assert.Equal(WebhookReceiveOutcome.Forbidden, await Sut().ReceiveAsync(ConnectionId, "nope", J(Inbound())));
    }

    [Fact]
    public async Task Receive_triggers_the_ai_once_per_conversation()
    {
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection());
        var two = "{\"results\":["
            + "{\"from\":\"96170123456\",\"to\":\"447000\",\"messageId\":\"a\",\"contact\":{\"name\":\"Z\"},\"message\":{\"type\":\"TEXT\",\"text\":\"one\"}},"
            + "{\"from\":\"96170123456\",\"to\":\"447000\",\"messageId\":\"b\",\"contact\":{\"name\":\"Z\"},\"message\":{\"type\":\"TEXT\",\"text\":\"two\"}}]}";
        Assert.Equal(WebhookReceiveOutcome.Accepted, await Sut().ReceiveAsync(ConnectionId, "tok", J(two)));
        AiTrigger.Verify(a => a.NotifyAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receive_routes_template_updates()
    {
        Integrations.Setup(i => i.GetByIdReadOnlyAsync(ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(Connection());
        _templatesRepo.Setup(t => t.FindInfobipTemplateClinicIdAsync("tpl1", It.IsAny<CancellationToken>())).ReturnsAsync(ClinicId);
        Templates.Setup(t => t.ApplyMetaEventAsync(It.IsAny<WhatsAppTemplateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppTemplateResponse(Guid.NewGuid(), ClinicId, "tpl1", "n", "c", "en", "approved", null, null, "b", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var body = J("{\"messageTemplateId\":\"tpl1\",\"messageTemplateName\":\"n\",\"change\":{\"type\":\"TEMPLATE_STATUS_UPDATE\",\"newStatus\":\"APPROVED\"}}");
        Assert.Equal(WebhookReceiveOutcome.Accepted, await Sut().ReceiveAsync(ConnectionId, "tok", body));
        Templates.Verify(t => t.ApplyMetaEventAsync(It.Is<WhatsAppTemplateEventRequest>(q => q.ClinicId == ClinicId && q.MetaTemplateId == "tpl1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Template_updates_for_unknown_templates_or_bodies_are_ignored()
    {
        Assert.Null(await Sut().ProcessTemplateUpdateAsync(J("[]")));
        var body = J("{\"messageTemplateId\":\"nope\",\"change\":{\"type\":\"TEMPLATE_STATUS_UPDATE\"}}");
        Assert.Null(await Sut().ProcessTemplateUpdateAsync(body));
        Templates.Verify(t => t.ApplyMetaEventAsync(It.IsAny<WhatsAppTemplateEventRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Account_events_need_the_configured_token()
    {
        var body = J("{\"messageTemplateId\":\"nope\"}");
        Assert.Equal(WebhookReceiveOutcome.Forbidden, await Sut().ReceiveAccountEventAsync("wrong", body));
        Assert.Equal(WebhookReceiveOutcome.Forbidden, await Sut().ReceiveAccountEventAsync(null, body));
        Assert.Equal(WebhookReceiveOutcome.Accepted, await Sut().ReceiveAccountEventAsync("acct-token", body));
        _configuration = new ConfigurationBuilder().Build();
        Assert.Equal(WebhookReceiveOutcome.NotFound, await Sut().ReceiveAccountEventAsync("acct-token", body));
    }
}
