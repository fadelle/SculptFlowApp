using Microsoft.Extensions.Configuration;
using PlasticSurgery.Business.Services.Inbox;

namespace PlasticSurgery.Tests.Services;

public class MessageServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _leadId = Guid.NewGuid();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<ILeadRepository> _leads = new();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<ICampaignRepository> _campaigns = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IWhatsAppService> _whatsApp = new();
    private readonly Mock<IChannelSender> _sender = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IMessageBillingService> _billing = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private IConfiguration _configuration = new ConfigurationBuilder().Build();
    private readonly MessageBillingHold _hold = new(Guid.Empty, "key", BillingSettlementTrigger.OnProviderAccepted);
    private readonly Lead _lead;

    public MessageServiceTests()
    {
        _lead = new Lead { Id = _leadId, ClinicId = _clinicId, FullName = "Ann Lee", Phone = "96170123456" };
        _sender.SetupGet(s => s.Channel).Returns(ConversationChannel.WhatsApp);
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("wamid.sent");
        _billing.Setup(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(_hold);
        _whatsApp.Setup(w => w.SendTemplateMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("wamid.tpl");
    }

    private MessageService Sut() => new(_conversations.Object, _messages.Object, _leads.Object, _templates.Object, _campaigns.Object, _uow.Object,
        _whatsApp.Object, [_sender.Object], _notifier.Object, _events.Object, _notifications.Object, _configuration, _billing.Object, _entitlements.Object);

    private Conversation Conv(string mode = ConversationMode.Ai, string channel = ConversationChannel.WhatsApp, bool windowOpen = true)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(), ClinicId = _clinicId, LeadId = _leadId, Lead = _lead, Channel = channel, Mode = mode, Status = ConversationStatus.Active,
            AiEnabled = mode == ConversationMode.Ai, HumanTakeover = mode == ConversationMode.Human,
            ServiceWindowExpiresAt = windowOpen ? DateTimeOffset.UtcNow.AddHours(5) : null
        };
        _conversations.Setup(r => r.GetWithLeadAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _conversations.Setup(r => r.GetAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    // ---------------------------------------------------------------- staff text

    [Fact]
    public async Task Send_validates_content_and_conversation()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendAsync(_clinicId, Guid.NewGuid(), new SendMessageRequest("  ")));
        Assert.Null(await Sut().SendAsync(_clinicId, Guid.NewGuid(), new SendMessageRequest("hi")));
    }

    [Fact]
    public async Task Send_delivers_bills_saves_and_takes_the_conversation_to_human()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        var r = await Sut().SendAsync(_clinicId, c.Id, new SendMessageRequest("hello"));

        Assert.Equal(("wamid.sent", MessageSenderType.Staff, MessageOrigin.Dashboard, "text"), (saved!.ExternalMessageId, saved.SenderType, saved.Origin, saved.MessageType));
        Assert.Equal((ConversationMode.Human, true), (c.Mode, c.HumanTakeover));
        Assert.NotNull(_lead.LastContactAt);
        _entitlements.Verify(e => e.EnsureCanSendMessagesAsync(_clinicId, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.ReserveOutboundAsync(It.Is<OutboundMessageBillingContext>(x => x.Kind == OutboundMessageKind.Text && x.ServiceWindowOpen && x.RecipientAddress == "96170123456" && x.MessageId == saved.Id), It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clinicId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NewMessageAsync(_clinicId, c.Id, saved.Id, _leadId, MessageDirection.Outbound, MessageSenderType.Staff, MessageOrigin.Dashboard, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(saved.Id, r!.Id);
    }

    [Fact]
    public async Task Send_in_human_mode_does_not_broadcast_a_mode_change()
    {
        var c = Conv(ConversationMode.Human);
        await Sut().SendAsync(_clinicId, c.Id, new SendMessageRequest("again", "image"));
        _notifier.Verify(n => n.ConversationModeChangedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_failure_releases_the_billing_hold_notifies_and_rethrows_without_saving()
    {
        var c = Conv();
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("blocked"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendAsync(_clinicId, c.Id, new SendMessageRequest("hello")));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SentAsync(It.IsAny<MessageBillingHold?>(), It.IsAny<CancellationToken>()), Times.Never);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.OutboundMessageFailed, It.IsAny<string>(), "Message failed to send to Ann Lee.", _leadId, c.Id, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_on_an_unsupported_channel_fails_after_reserving_and_releases()
    {
        var c = Conv(channel: ConversationChannel.Instagram);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clinicId, c.Id, new SendMessageRequest("hello")));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_is_blocked_before_anything_happens_when_the_clinic_may_not_send()
    {
        var c = Conv();
        _entitlements.Setup(e => e.EnsureCanSendMessagesAsync(_clinicId, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("messages", "No active plan"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SendAsync(_clinicId, c.Id, new SendMessageRequest("hello")));
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- AI reply

    [Fact]
    public async Task AiReply_requires_ai_mode_and_content()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendAiReplyAsync(_clinicId, Guid.NewGuid(), ""));
        Assert.Null(await Sut().SendAiReplyAsync(_clinicId, Guid.NewGuid(), "hi"));
        var human = Conv(ConversationMode.Human);
        await Assert.ThrowsAsync<ConversationNotInAiModeException>(() => Sut().SendAiReplyAsync(_clinicId, human.Id, "hi"));
    }

    [Fact]
    public async Task AiReply_is_sent_as_ai_without_changing_mode()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().SendAiReplyAsync(_clinicId, c.Id, "Sure, we can help");
        Assert.Equal((MessageSenderType.Ai, MessageOrigin.Ai, true), (saved!.SenderType, saved.Origin, saved.IsAiGenerated));
        Assert.Equal(ConversationMode.Ai, c.Mode);
        _events.Verify(e => e.Log(_clinicId, EventTypes.AiMessageSent, _leadId, c.Id, null, MessageOrigin.Ai, "{}"), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AiReply_failure_releases_the_hold_but_does_not_notify_staff()
    {
        var c = Conv();
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("x"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendAiReplyAsync(_clinicId, c.Id, "hi"));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- templates

    private WhatsAppTemplate Template(string status = WhatsAppTemplateStatus.Approved, string? provider = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, Name = "promo", Language = "en", Body = "Hi {{1}}, offer {{2}}!", Status = status,
        Category = "MARKETING", CurrentCategory = "UTILITY", Provider = provider
    };

    private Template_ Setup(WhatsAppTemplate t) { _templates.Setup(r => r.GetAsync(_clinicId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t); return new Template_(t); }

    private sealed record Template_(WhatsAppTemplate T);

    [Fact]
    public async Task Template_is_sent_rendered_billed_by_current_category_and_marks_human_mode()
    {
        var c = Conv();
        var t = Template();
        Setup(t);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        var r = await Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(t.Id, ["Ann", "20%"]));

        Assert.Equal(("template", "Hi Ann, offer 20%!", t.Id, MessageOrigin.Dashboard), (saved!.MessageType, saved.Content, saved.WhatsAppTemplateId, saved.Origin));
        _whatsApp.Verify(w => w.SendTemplateMessageAsync(_clinicId, "96170123456", "promo", "en", It.Is<IReadOnlyList<string>>(p => p.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.ReserveOutboundAsync(It.Is<OutboundMessageBillingContext>(x => x.Kind == OutboundMessageKind.Template && x.TemplateCategory == "UTILITY"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ConversationMode.Human, c.Mode);
        Assert.NotNull(r);
    }

    [Fact]
    public async Task Campaign_template_records_campaign_and_recipient_and_uses_campaign_event()
    {
        var c = Conv(ConversationMode.Human);
        var t = Template();
        t.CurrentCategory = null;
        Setup(t);
        var campaignId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        await Sut().SendCampaignTemplateAsync(_clinicId, c.Id, t.Id, ["a", "b"], campaignId, recipientId);

        Assert.Equal((campaignId, recipientId, MessageOrigin.Campaign), (saved!.CampaignId, saved.CampaignRecipientId, saved.Origin));
        _events.Verify(e => e.Log(_clinicId, EventTypes.CampaignMessageSent, _leadId, c.Id, null, MessageOrigin.Campaign, "{}"), Times.Once);
        _billing.Verify(b => b.ReserveOutboundAsync(It.Is<OutboundMessageBillingContext>(x => x.TemplateCategory == "MARKETING" && x.CampaignId == campaignId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Template_send_guards()
    {
        Assert.Null(await Sut().SendTemplateAsync(_clinicId, Guid.NewGuid(), new SendTemplateMessageRequest(Guid.NewGuid(), null)));

        var tg = Conv(channel: ConversationChannel.Telegram);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clinicId, tg.Id, new SendTemplateMessageRequest(Guid.NewGuid(), null)));

        var c = Conv();
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(Guid.NewGuid(), null)));

        var pending = Template(WhatsAppTemplateStatus.Pending);
        Setup(pending);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(pending.Id, null)));
        Assert.Contains("isn't approved", ex.Message);

        var other = Template(provider: "infobip");
        Setup(other);
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(other.Id, null)));
        Assert.Contains("previous WhatsApp setup", ex2.Message);

        _lead.Phone = null;
        var ok = Template();
        Setup(ok);
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(ok.Id, null)));
        Assert.Contains("no phone number", ex3.Message);
        _billing.Verify(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Template_provider_follows_configuration()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = " Infobip " }).Build();
        var c = Conv();
        var t = Template(provider: "infobip");
        Setup(t);
        Assert.NotNull(await Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(t.Id, null)));
    }

    [Fact]
    public async Task Template_provider_failure_releases_the_hold_and_notifies()
    {
        var c = Conv();
        var t = Template();
        Setup(t);
        _whatsApp.Setup(w => w.SendTemplateMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("rejected"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendTemplateAsync(_clinicId, c.Id, new SendTemplateMessageRequest(t.Id, null)));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.OutboundMessageFailed, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RenderTemplatePreview_substitutes_numbered_placeholders()
    {
        Assert.Equal("Hi Ann, offer 20%!", MessageService.RenderTemplatePreview("Hi {{1}}, offer {{2}}!", ["Ann", "20%"]));
        Assert.Equal("Hi {{1}}", MessageService.RenderTemplatePreview("Hi {{1}}", []));
    }

    // ---------------------------------------------------------------- ingest

    private IngestMessageRequest Ingest(Guid conversationId, string type, string? externalId = "ext1", string? messageType = null, bool newLead = false) =>
        new(_clinicId, conversationId, _leadId, type, ConversationChannel.WhatsApp, "hello there", externalId, null, null, null, messageType, null, null, null)
        { LeadWasNewlyCreated = newLead };

    [Fact]
    public async Task Ingest_rejects_unknown_event_types_and_unknown_conversations()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(Ingest(Guid.NewGuid(), "weird")));
        var r = await Sut().IngestAsync(Ingest(Guid.NewGuid(), IngestEventType.CustomerMessage));
        Assert.False(r.Found);
    }

    [Fact]
    public async Task Customer_message_opens_the_service_window_and_is_ai_eligible_in_ai_mode()
    {
        var c = Conv(windowOpen: false);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        _leads.Setup(l => l.GetByIdAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync(_lead);

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.True(r.Found);
        Assert.False(r.Deduplicated);
        Assert.True(r.AiEligible);
        Assert.Equal(ConversationMode.Ai, r.ConversationMode);
        Assert.Equal((MessageDirection.Inbound, MessageSenderType.Lead, MessageOrigin.WhatsAppCustomer), (saved!.Direction, saved.SenderType, saved.Origin));
        Assert.NotNull(saved.ReceivedAt);
        Assert.NotNull(c.LastCustomerMessageAt);
        Assert.InRange(c.ServiceWindowExpiresAt!.Value, DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
        _events.Verify(e => e.Log(_clinicId, EventTypes.CustomerMessageReceived, _leadId, c.Id, null, MessageOrigin.WhatsAppCustomer, "{}"), Times.Once);
    }

    [Theory]
    [InlineData(ConversationMode.Human, "text", false)]
    [InlineData(ConversationMode.Approval, "text", false)]
    [InlineData(ConversationMode.Ai, "image", false)]
    [InlineData(ConversationMode.Ai, "interactive", true)]
    [InlineData(ConversationMode.Ai, "TEXT", true)]
    public async Task AiEligibility_depends_on_mode_and_message_type(string mode, string messageType, bool eligible)
    {
        var c = Conv(mode);
        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, messageType: messageType));
        Assert.Equal(eligible, r.AiEligible);
    }

    [Fact]
    public async Task Telegram_customer_message_does_not_touch_the_whatsapp_window()
    {
        var c = Conv(channel: ConversationChannel.Telegram, windowOpen: false);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { Channel = ConversationChannel.Telegram });
        Assert.Null(c.ServiceWindowExpiresAt);
        Assert.Equal(MessageOrigin.TelegramCustomer, saved!.Origin);
    }

    [Fact]
    public async Task Duplicate_external_ids_are_not_stored_twice()
    {
        var c = Conv();
        var existing = new Message { Id = Guid.NewGuid(), ConversationId = c.Id, Content = "first" };
        _messages.Setup(m => m.FindByExternalIdAsync(_clinicId, "whatsapp", "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));
        Assert.True(r.Deduplicated);
        Assert.False(r.AiEligible);
        Assert.Equal(existing.Id, r.Message!.Id);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
    }

    [Fact]
    public async Task Business_app_echo_moves_the_conversation_to_human_and_broadcasts()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.BusinessAppEcho));
        Assert.Equal((MessageDirection.Outbound, MessageSenderType.Staff, MessageOrigin.WhatsAppBusinessApp), (saved!.Direction, saved.SenderType, saved.Origin));
        Assert.Equal(ConversationMode.Human, c.Mode);
        Assert.False(r.AiEligible);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clinicId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ai_message_is_stored_as_ai_generated()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.AiMessage));
        Assert.Equal((true, MessageSenderType.Ai), (saved!.IsAiGenerated, saved.SenderType));
        Assert.Equal(ConversationMode.Ai, c.Mode);
    }

    [Fact]
    public async Task First_message_of_a_new_lead_raises_a_notification_and_campaign_replies_are_recorded()
    {
        var c = Conv();
        _leads.Setup(l => l.GetByIdAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync(_lead);
        var recipient = new CampaignRecipient { Id = Guid.NewGuid(), Campaign = new Campaign { Name = "Spring" } };
        _campaigns.Setup(x => x.FindLatestAwaitingReplyAsync(_clinicId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(recipient);

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, newLead: true));

        Assert.NotNull(recipient.RepliedAt);
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.NewLead, "New lead", "Ann Lee sent their first message.", _leadId, c.Id, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.CampaignReply, "Campaign reply", "Ann Lee replied to \"Spring\".", _leadId, c.Id, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- status updates

    private Message Outbound(string? status = "sent", Guid? recipientId = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, ConversationId = Guid.NewGuid(), LeadId = _leadId, Direction = MessageDirection.Outbound,
        Channel = "whatsapp", ExternalMessageId = "ext1", DeliveryStatus = status, CampaignRecipientId = recipientId
    };

    private IngestMessageRequest Status(string status, string? code = null, string? reason = null) =>
        new(_clinicId, Guid.NewGuid(), _leadId, IngestEventType.StatusUpdate, "whatsapp", null, "ext1", status, null, null, null, null, code, reason);

    private Message FoundMessage(Message m)
    {
        _messages.Setup(r => r.FindByExternalIdAsync(_clinicId, "whatsapp", "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(m);
        return m;
    }

    [Fact]
    public async Task Status_update_requires_an_external_id_and_a_known_message()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(Status("sent") with { ExternalMessageId = null }));
        Assert.False((await Sut().IngestAsync(Status("sent"))).Found);
    }

    [Theory]
    [InlineData("delivered")]
    [InlineData("read")]
    public async Task Status_update_advances_and_stamps_times(string status)
    {
        var m = FoundMessage(Outbound("sent"));
        var r = await Sut().IngestAsync(Status(status));
        Assert.True(r.Found);
        Assert.Equal(status, m.DeliveryStatus);
        Assert.NotNull(status == "read" ? m.ReadAt : m.DeliveredAt);
        _billing.Verify(b => b.DeliveryStatusChangedAsync(m, status, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.MessageStatusUpdatedAsync(_clinicId, m.ConversationId, m.Id, status, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_late_earlier_step_keeps_the_stronger_status_but_fills_its_timestamp()
    {
        var m = FoundMessage(Outbound("read"));
        await Sut().IngestAsync(Status("delivered"));
        Assert.Equal("read", m.DeliveryStatus);
        Assert.NotNull(m.DeliveredAt);
        _notifier.Verify(n => n.MessageStatusUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Repeating_the_same_status_is_deduplicated_but_still_tells_billing()
    {
        var m = FoundMessage(Outbound("delivered"));
        var r = await Sut().IngestAsync(Status("DELIVERED"));
        Assert.True(r.Deduplicated);
        _billing.Verify(b => b.DeliveryStatusChangedAsync(m, "delivered", It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task First_failure_stores_the_reason_and_notifies_staff_once()
    {
        _leads.Setup(l => l.GetFullNameAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync("Ann Lee");
        var m = FoundMessage(Outbound("sent"));
        await Sut().IngestAsync(Status("failed", "131026", "Undeliverable"));
        Assert.Equal(("failed", "131026", "Undeliverable"), (m.DeliveryStatus, m.FailureCode, m.FailureReason));
        Assert.NotNull(m.FailedAt);
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.OutboundMessageFailed, "Message failed to send", "Message failed to send to Ann Lee.", _leadId, m.ConversationId, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        await Sut().IngestAsync(Status("sent")); // not a repeat of failed; but a second failure must not notify again
        m.DeliveryStatus = "sent";
        await Sut().IngestAsync(Status("failed"));
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.OutboundMessageFailed, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleted_status_stamps_deleted_at()
    {
        var m = FoundMessage(Outbound("sent"));
        await Sut().IngestAsync(Status("deleted"));
        Assert.NotNull(m.DeletedAt);
    }

    [Theory]
    [InlineData("delivered", CampaignRecipientStatus.Delivered)]
    [InlineData("read", CampaignRecipientStatus.Read)]
    [InlineData("failed", CampaignRecipientStatus.Failed)]
    public async Task Campaign_recipients_follow_message_status(string status, string expected)
    {
        var recipient = new CampaignRecipient { Id = Guid.NewGuid(), Status = CampaignRecipientStatus.Sent };
        _campaigns.Setup(c => c.GetRecipientAsync(recipient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(recipient);
        FoundMessage(Outbound("sent", recipient.Id));
        await Sut().IngestAsync(Status(status, "c", "r"));
        Assert.Equal(expected, recipient.Status);
        Assert.Equal(status == "failed" ? "r" : null, recipient.FailureReason);
    }

    [Fact]
    public async Task A_late_step_does_not_regress_the_campaign_recipient()
    {
        var recipient = new CampaignRecipient { Id = Guid.NewGuid(), Status = CampaignRecipientStatus.Read };
        _campaigns.Setup(c => c.GetRecipientAsync(recipient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(recipient);
        FoundMessage(Outbound("read", recipient.Id));
        await Sut().IngestAsync(Status("delivered"));
        Assert.Equal(CampaignRecipientStatus.Read, recipient.Status);
    }
}
