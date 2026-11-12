using Microsoft.Extensions.Configuration;
using Omni.App.Business.Services.Inbox;
using Omni.App.Entities.Dtos.Monitoring;

namespace Omni.App.Tests.Services;

public class MessageServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _contactId = Guid.NewGuid();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IContactRepository> _contacts = new();
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
    private readonly Mock<IInboxSettingsService> _inboxSettings = new();
    private readonly Mock<IMessageTracer> _tracer = new();
    private readonly Mock<IConversationService> _conversationService = new();
    private readonly Mock<IOptOutService> _optOut = new();
    private readonly Mock<IAutoReplyService> _autoReplies = new();
    private readonly Mock<IBusinessTypeService> _businessTypes = PipelineKit.Types();
    private readonly Mock<IConfigManager> _config = ConfigWithRetry();

    private static Mock<IConfigManager> ConfigWithRetry()
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.AiRetryUnansweredAfterSeconds).Returns(120);
        return config;
    }
    private readonly MessageBillingHold _hold = new(Guid.Empty, "key", BillingSettlementTrigger.OnProviderAccepted);
    private readonly Contact _contact;

    public MessageServiceTests()
    {
        InboxSettings(planIncludesBot: true, reopenedConversations: true);
        AutoReply(AutoReplyChoice.Skip(AutoReplyRules.SkipOff));
        // A stop/start word changes the contact's opt-out unless a test says it was already so.
        _optOut.Setup(o => o.Apply(It.IsAny<Contact>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<Guid?>())).Returns(true);
        _contact = new Contact { Id = _contactId, ClientId = _clientId, FullName = "Ann Lee", Phone = "96170123456" };
        _sender.SetupGet(s => s.Channel).Returns(ConversationChannel.WhatsApp);
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("wamid.sent");
        _billing.Setup(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(_hold);
        // The client's WhatsApp is connected unless a test says otherwise (template sends check it first).
        _whatsApp.Setup(w => w.IsReadyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _whatsApp.Setup(w => w.SendTemplateMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("wamid.tpl");
    }

    private MessageService Sut() => new(_conversations.Object, _messages.Object, _contacts.Object, _templates.Object, _campaigns.Object, _uow.Object,
        _whatsApp.Object, [_sender.Object], _notifier.Object, _events.Object, _notifications.Object, _billing.Object, _entitlements.Object, _inboxSettings.Object, _tracer.Object,
        _conversationService.Object, _optOut.Object, _autoReplies.Object, _businessTypes.Object, _config.Object);

    private void AutoReply(AutoReplyChoice choice) =>
        _autoReplies.Setup(a => a.ChooseAsync(_clientId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(choice);

    /// <summary>The account behind every channel has an answering bot, unless the plan lacks the AI assistant or the channel
    /// is in <paramref name="inactiveBotOn"/>.</summary>
    private void InboxSettings(bool planIncludesBot, bool reopenedConversations, params string[] inactiveBotOn) =>
        _inboxSettings.Setup(a => a.ResolveAsync(_clientId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string channel, CancellationToken _) => new AccountAiDecision(Guid.NewGuid(), Guid.NewGuid(),
                !planIncludesBot ? AiBotBlockReason.PlanWithoutAi : inactiveBotOn.Contains(channel) ? AiBotBlockReason.BotInactive : null,
                true, reopenedConversations));

    private Conversation Conv(string mode = ConversationMode.Ai, string channel = ConversationChannel.WhatsApp, bool windowOpen = true)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(), ClientId = _clientId, ContactId = _contactId, Contact = _contact, Channel = channel, Mode = mode, Status = ConversationStatus.Active,
            AiEnabled = mode == ConversationMode.Ai, HumanTakeover = mode == ConversationMode.Human,
            ServiceWindowExpiresAt = windowOpen ? DateTimeOffset.UtcNow.AddHours(5) : null
        };
        _conversations.Setup(r => r.GetWithContactAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _conversations.Setup(r => r.GetAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    // ---------------------------------------------------------------- member text

    [Fact]
    public async Task Send_validates_content_and_conversation()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendAsync(_clientId, Guid.NewGuid(), new SendMessageRequest("  ")));
        Assert.Null(await Sut().SendAsync(_clientId, Guid.NewGuid(), new SendMessageRequest("hi")));
    }

    [Fact]
    public async Task Send_delivers_bills_saves_and_takes_the_conversation_to_human()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        var r = await Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello"));

        Assert.Equal(("wamid.sent", MessageSenderType.Member, MessageOrigin.Dashboard, "text"), (saved!.ExternalMessageId, saved.SenderType, saved.Origin, saved.MessageType));
        Assert.Equal((ConversationMode.Human, true), (c.Mode, c.HumanTakeover));
        Assert.NotNull(_contact.LastContactAt);
        _entitlements.Verify(e => e.EnsureCanSendMessagesAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.ReserveOutboundAsync(It.Is<OutboundMessageBillingContext>(x => x.Kind == OutboundMessageKind.Text && x.ServiceWindowOpen && x.RecipientAddress == "96170123456" && x.MessageId == saved.Id), It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NewMessageAsync(_clientId, c.Id, saved.Id, _contactId, MessageDirection.Outbound, MessageSenderType.Member, MessageOrigin.Dashboard, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(saved.Id, r!.Id);
    }

    [Fact]
    public async Task Send_in_human_mode_does_not_broadcast_a_mode_change()
    {
        var c = Conv(ConversationMode.Human);
        await Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("again", "image"));
        _notifier.Verify(n => n.ConversationModeChangedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_failure_releases_the_billing_hold_notifies_and_rethrows_without_saving()
    {
        var c = Conv();
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("blocked"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello")));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SentAsync(It.IsAny<MessageBillingHold?>(), It.IsAny<CancellationToken>()), Times.Never);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        // The member saw the refusal on screen: no notice for the whole team (QA t8-14).
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.OutboundMessageFailed, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_on_an_unsupported_channel_fails_after_reserving_and_releases()
    {
        var c = Conv(channel: ConversationChannel.Instagram);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello")));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_is_blocked_before_anything_happens_when_the_client_may_not_send()
    {
        var c = Conv();
        _entitlements.Setup(e => e.EnsureCanSendMessagesAsync(_clientId, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("messages", "No active plan"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello")));
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- AI reply

    [Fact]
    public async Task AiReply_requires_ai_mode_and_content()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendAiReplyAsync(_clientId, Guid.NewGuid(), ""));
        Assert.Null(await Sut().SendAiReplyAsync(_clientId, Guid.NewGuid(), "hi"));
        var human = Conv(ConversationMode.Human);
        await Assert.ThrowsAsync<ConversationNotInAiModeException>(() => Sut().SendAiReplyAsync(_clientId, human.Id, "hi"));
    }

    [Fact]
    public async Task AiReply_is_sent_as_ai_without_changing_mode()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().SendAiReplyAsync(_clientId, c.Id, "Sure, we can help");
        Assert.Equal((MessageSenderType.Ai, MessageOrigin.Ai, true), (saved!.SenderType, saved.Origin, saved.IsAiGenerated));
        Assert.Equal(ConversationMode.Ai, c.Mode);
        _events.Verify(e => e.Log(_clientId, EventTypes.AiMessageSent, _contactId, c.Id, null, MessageOrigin.Ai, "{}"), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_ai_reply_is_kept_as_a_failed_message_and_hands_the_conversation_to_the_team()
    {
        // QA t10-07: with a broken WhatsApp token every AI reply vanished: no bubble, no notification, still "AI is replying".
        var c = Conv();
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("OAuthException 190"));
        Message? kept = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => kept = m);

        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendAiReplyAsync(_clientId, c.Id, "We open at 9 tomorrow."));

        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(("We open at 9 tomorrow.", "failed", MessageSenderType.Ai, true), (kept!.Content, kept.DeliveryStatus, kept.SenderType, kept.FailedAt is not null));
        Assert.Null(kept.ExternalMessageId);
        _notifier.Verify(n => n.NewMessageAsync(_clientId, c.Id, kept.Id, It.IsAny<Guid>(), MessageDirection.Outbound, MessageSenderType.Ai, MessageOrigin.Ai,
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        // One hand-over (it notifies the members); no separate "send failed" notification on top.
        _conversationService.Verify(s => s.HandOverFailedAiAsync(_clientId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- templates

    private WhatsAppTemplate Template(string status = WhatsAppTemplateStatus.Approved) => new()
    {
        Id = Guid.NewGuid(), ClientId = _clientId, Name = "promo", Language = "en", Body = "Hi {{1}}, offer {{2}}!", Status = status,
        Category = "MARKETING", CurrentCategory = "UTILITY"
    };

    private Template_ Setup(WhatsAppTemplate t) { _templates.Setup(r => r.GetReadOnlyAsync(_clientId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t); return new Template_(t); }

    private sealed record Template_(WhatsAppTemplate T);

    [Theory]
    [InlineData(PipelineKit.New, PipelineKit.Contacted)]          // the team's first reply moves a new contact on
    [InlineData(PipelineKit.Contacted, PipelineKit.Contacted)]    // later replies leave it
    [InlineData(PipelineKit.Booked, PipelineKit.Booked)]          // any other stage is left alone
    public async Task A_members_reply_moves_a_contact_out_of_the_entry_stage(string from, string expected)
    {
        _contact.StageKey = from;
        var c = Conv();
        await Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello"));
        Assert.Equal(expected, _contact.StageKey);
    }

    [Fact]
    public async Task A_members_template_moves_a_new_contact_on_but_an_ai_reply_campaign_or_reminder_doesnt()
    {
        var t = Template();
        Setup(t);

        _contact.StageKey = PipelineKit.New;
        await Sut().SendTemplateAsync(_clientId, Conv().Id, new SendTemplateMessageRequest(t.Id, ["Ann", "20%"]));
        Assert.Equal(PipelineKit.Contacted, _contact.StageKey);

        _contact.StageKey = PipelineKit.New;
        await Sut().SendCampaignTemplateAsync(_clientId, Conv().Id, t.Id, ["Ann", "20%"], Guid.NewGuid(), Guid.NewGuid());
        await Sut().SendReminderTemplateAsync(_clientId, Conv().Id, t.Id, ["Ann", "20%"]);
        await Sut().SendAiReplyAsync(_clientId, Conv().Id, "Hello from the bot");
        Assert.Equal(PipelineKit.New, _contact.StageKey);
    }

    [Fact]
    public async Task A_type_with_a_single_open_stage_keeps_the_contact_on_reply()
    {
        _businessTypes.Use(new ClientPipeline([new("only", StageCategory.Open, "Only"), new("won", StageCategory.Converted, "Won")]));
        _contact.StageKey = "only";
        await Sut().SendAsync(_clientId, Conv().Id, new SendMessageRequest("hello"));
        Assert.Equal("only", _contact.StageKey);
    }

    [Fact]
    public async Task Template_is_sent_rendered_billed_by_current_category_and_marks_human_mode()
    {
        var c = Conv();
        var t = Template();
        Setup(t);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        var r = await Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(t.Id, ["Ann", "20%"]));

        Assert.Equal(("template", "Hi Ann, offer 20%!", t.Id, MessageOrigin.Dashboard), (saved!.MessageType, saved.Content, saved.WhatsAppTemplateId, saved.Origin));
        _whatsApp.Verify(w => w.SendTemplateMessageAsync(_clientId, "96170123456", "promo", "en", It.Is<IReadOnlyList<string>>(p => p.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
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

        await Sut().SendCampaignTemplateAsync(_clientId, c.Id, t.Id, ["a", "b"], campaignId, recipientId);

        Assert.Equal((campaignId, recipientId, MessageOrigin.Campaign), (saved!.CampaignId, saved.CampaignRecipientId, saved.Origin));
        _events.Verify(e => e.Log(_clientId, EventTypes.CampaignMessageSent, _contactId, c.Id, null, MessageOrigin.Campaign, "{}"), Times.Once);
        _billing.Verify(b => b.ReserveOutboundAsync(It.Is<OutboundMessageBillingContext>(x => x.TemplateCategory == "MARKETING" && x.CampaignId == campaignId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Template_send_guards()
    {
        Assert.Null(await Sut().SendTemplateAsync(_clientId, Guid.NewGuid(), new SendTemplateMessageRequest(Guid.NewGuid(), null)));

        var tg = Conv(channel: ConversationChannel.Telegram);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clientId, tg.Id, new SendTemplateMessageRequest(Guid.NewGuid(), null)));

        var c = Conv();
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(Guid.NewGuid(), null)));

        var pending = Template(WhatsAppTemplateStatus.Pending);
        Setup(pending);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(pending.Id, null)));
        Assert.Contains("isn't approved", TextKit.English(ex));

        // QA t5-ops-05: without a connected WhatsApp the send is refused before billing is asked anything.
        _whatsApp.Setup(w => w.IsReadyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var notConnected = await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(pending.Id, null)));
        Assert.Equal("channels.whatsApp.errors.notConnected", notConnected.Message);
        _whatsApp.Setup(w => w.IsReadyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _contact.Phone = null;
        var ok = Template();
        Setup(ok);
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(ok.Id, null)));
        Assert.Contains("no phone number", TextKit.English(ex3));
        _billing.Verify(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Template_provider_failure_releases_the_hold_and_notifies()
    {
        var c = Conv();
        var t = Template();
        Setup(t);
        _whatsApp.Setup(w => w.SendTemplateMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("rejected"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(t.Id, null)));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        // The member who sent it sees the refusal; the team isn't told (QA t8-14)...
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.OutboundMessageFailed, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        // ...but a reminder nobody is watching is.
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendReminderTemplateAsync(_clientId, c.Id, t.Id, []));
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.OutboundMessageFailed, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RenderTemplatePreview_substitutes_numbered_placeholders()
    {
        Assert.Equal("Hi Ann, offer 20%!", MessageService.RenderTemplatePreview("Hi {{1}}, offer {{2}}!", ["Ann", "20%"]));
        Assert.Equal("Hi {{1}}", MessageService.RenderTemplatePreview("Hi {{1}}", []));
    }

    // ---------------------------------------------------------------- ingest

    private IngestMessageRequest Ingest(Guid conversationId, string type, string? externalId = "ext1", string? messageType = null, bool newContact = false) =>
        new(_clientId, conversationId, _contactId, type, ConversationChannel.WhatsApp, "hello there", externalId, null, null, null, messageType, null, null, null)
        { ContactWasNewlyCreated = newContact };

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
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.True(r.Found);
        Assert.False(r.Deduplicated);
        Assert.True(r.AiEligible);
        Assert.Equal(ConversationMode.Ai, r.ConversationMode);
        Assert.Equal((MessageDirection.Inbound, MessageSenderType.Contact, MessageOrigin.WhatsAppCustomer), (saved!.Direction, saved.SenderType, saved.Origin));
        Assert.NotNull(saved.ReceivedAt);
        Assert.NotNull(c.LastCustomerMessageAt);
        Assert.InRange(c.ServiceWindowExpiresAt!.Value, DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
        _events.Verify(e => e.Log(_clientId, EventTypes.ContactMessageReceived, _contactId, c.Id, null, MessageOrigin.WhatsAppCustomer, "{}"), Times.Once);
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

    [Theory]
    [InlineData("interactive", """{"type":"nfm_reply","nfm_reply":{"name":"flow","response_json":"{}"}}""")]
    [InlineData("button", """{"payload":"x"}""")]
    public async Task An_interactive_reply_without_text_is_saved_but_not_given_to_the_bot(string messageType, string metadata)
    {
        // QA t10-09: a WhatsApp Flows answer has no text, and the bot answered it with "How can I help you?".
        var c = Conv(ConversationMode.Ai);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, messageType: messageType) with { Content = null, MetadataJson = metadata });
        Assert.False(r.AiEligible);
        Assert.NotNull(saved);
        Assert.Equal(ConversationMode.Ai, c.Mode); // it stays with the bot for the next message
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

    [Theory]
    [InlineData(IngestEventType.CustomerMessage, true)]
    [InlineData(IngestEventType.BusinessAppEcho, false)]
    [InlineData(IngestEventType.AiMessage, false)]
    public async Task Only_a_customer_message_reopens_a_closed_conversation(string eventType, bool reopens)
    {
        var c = Conv();
        c.Status = ConversationStatus.Closed;

        await Sut().IngestAsync(Ingest(c.Id, eventType));

        Assert.Equal(reopens ? ConversationStatus.Active : ConversationStatus.Closed, c.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConversationReopened, _contactId, c.Id, null, MessageOrigin.WhatsAppCustomer, "{}"), reopens ? Times.Once() : Times.Never());
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clientId, c.Id, It.IsAny<CancellationToken>()), reopens ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(true, true, ConversationMode.Ai)]      // bot on for reopened: stays with the AI
    [InlineData(true, false, ConversationMode.Human)]  // client sends reopened conversations to members
    [InlineData(false, true, ConversationMode.Human)]  // plan without the AI assistant: always members
    public async Task A_reopened_conversation_follows_the_clients_ai_bot_setting(bool planIncludesBot, bool reopenedOn, string expectedMode)
    {
        InboxSettings(planIncludesBot, reopenedOn);
        var c = Conv();
        c.Status = ConversationStatus.Closed;

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.Equal((ConversationStatus.Active, expectedMode, expectedMode == ConversationMode.Ai), (c.Status, c.Mode, r.AiEligible));
        Assert.Equal(expectedMode, r.ConversationMode);
        var handedOff = expectedMode == ConversationMode.Human;
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), handedOff ? Times.Once() : Times.Never());
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, _contactId, c.Id, null, MessageOrigin.System, It.IsAny<string>()), handedOff ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task A_customer_message_on_an_account_whose_bot_is_inactive_goes_to_human_without_touching_other_accounts()
    {
        InboxSettings(planIncludesBot: true, reopenedConversations: true, ConversationChannel.Telegram);
        var telegram = Conv(channel: ConversationChannel.Telegram);
        var whatsApp = Conv();

        var r = await Sut().IngestAsync(Ingest(telegram.Id, IngestEventType.CustomerMessage) with { Channel = ConversationChannel.Telegram });
        Assert.Equal((ConversationMode.Human, false), (telegram.Mode, r.AiEligible));
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, _contactId, telegram.Id, null, MessageOrigin.System, "{\"reason\":\"bot_inactive\"}"), Times.Once);

        var w = await Sut().IngestAsync(Ingest(whatsApp.Id, IngestEventType.CustomerMessage, externalId: "e2"));
        Assert.Equal((ConversationMode.Ai, true), (whatsApp.Mode, w.AiEligible));
    }

    // ---------------------------------------------------------------- the bot's handoff message

    [Fact]
    public async Task The_handoff_message_goes_out_as_an_automatic_message_without_changing_the_mode()
    {
        var c = Conv(ConversationMode.Human);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        var bot = Guid.NewGuid();

        var r = await Sut().SendHandoffMessageAsync(_clientId, c.Id, bot, "Someone will contact you shortly.");

        Assert.Equal((MessageSenderType.System, MessageOrigin.System, "Someone will contact you shortly.", ConversationMode.Human),
            (saved!.SenderType, saved.Origin, saved.Content, c.Mode));
        Assert.Equal(saved.Id, r!.Id);
        _events.Verify(e => e.Log(_clientId, EventTypes.HandoffMessageSent, _contactId, c.Id, null, MessageOrigin.System, $"{{\"botId\":\"{bot}\"}}"), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_handoff_message_respects_the_whatsapp_window_and_never_fails_the_handoff()
    {
        var closed = Conv(ConversationMode.Human, windowOpen: false);
        Assert.Null(await Sut().SendHandoffMessageAsync(_clientId, closed.Id, Guid.NewGuid(), "Bye"));
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tracer.Verify(t => t.Step(TraceSteps.HandoffMessageSkipped, TraceOutcomes.Skipped, It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()), Times.Once);

        // Telegram has no window; a provider failure is traced and swallowed.
        var telegram = Conv(ConversationMode.Human, channel: ConversationChannel.Telegram, windowOpen: false);
        _sender.SetupGet(s => s.Channel).Returns(ConversationChannel.Telegram);
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("blocked"));
        Assert.Null(await Sut().SendHandoffMessageAsync(_clientId, telegram.Id, Guid.NewGuid(), "Bye"));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _tracer.Verify(t => t.Step(TraceSteps.SendBlocked, TraceOutcomes.Failed, It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()), Times.Once);

        Assert.Null(await Sut().SendHandoffMessageAsync(_clientId, Guid.NewGuid(), Guid.NewGuid(), "Bye"));
    }

    [Fact]
    public async Task A_customer_message_in_an_ai_conversation_goes_to_human_when_the_plan_lost_the_ai_assistant()
    {
        InboxSettings(planIncludesBot: false, reopenedConversations: true);
        var c = Conv();

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.Equal((ConversationMode.Human, false, true), (c.Mode, r.AiEligible, c.HumanTakeover));
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, _contactId, c.Id, null, MessageOrigin.System, "{\"reason\":\"plan_without_ai_assistant\"}"), Times.Once);
    }

    [Fact]
    public async Task The_reopened_switch_does_not_touch_an_active_ai_conversation_or_a_human_one()
    {
        InboxSettings(planIncludesBot: true, reopenedConversations: false);
        var active = Conv();
        Assert.True((await Sut().IngestAsync(Ingest(active.Id, IngestEventType.CustomerMessage))).AiEligible);
        Assert.Equal(ConversationMode.Ai, active.Mode);

        var human = Conv(ConversationMode.Human);
        human.Status = ConversationStatus.Closed;
        await Sut().IngestAsync(Ingest(human.Id, IngestEventType.CustomerMessage, externalId: "ext2"));
        Assert.Equal(ConversationMode.Human, human.Mode);
        _notifier.Verify(n => n.ConversationModeChangedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_customer_message_in_an_active_conversation_is_not_reported_as_a_reopen()
    {
        var c = Conv();

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.Equal(ConversationStatus.Active, c.Status);
        _events.Verify(e => e.Log(It.IsAny<Guid>(), EventTypes.ConversationReopened, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        _notifier.Verify(n => n.ConversationUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Duplicate_external_ids_are_not_stored_twice()
    {
        var c = Conv();
        var existing = new Message { Id = Guid.NewGuid(), ConversationId = c.Id, Content = "first" };
        _messages.Setup(m => m.FindByExternalIdAsync(_clientId, "whatsapp", "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
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
        Assert.Equal((MessageDirection.Outbound, MessageSenderType.Member, MessageOrigin.WhatsAppBusinessApp), (saved!.Direction, saved.SenderType, saved.Origin));
        Assert.Equal(ConversationMode.Human, c.Mode);
        Assert.False(r.AiEligible);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task First_message_of_a_new_contact_raises_a_notification_and_campaign_replies_are_recorded()
    {
        var c = Conv();
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        var recipient = new CampaignRecipient { Id = Guid.NewGuid(), Campaign = new Campaign { Name = "Spring" } };
        _campaigns.Setup(x => x.FindLatestAwaitingReplyAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(recipient);

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, newContact: true));

        Assert.NotNull(recipient.RepliedAt);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.NewContact, It.Is<NotificationText>(t => TextKit.Reads(t, "New contact", "Ann Lee sent their first message.")), _contactId, c.Id, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.CampaignReply, It.Is<NotificationText>(t => TextKit.Reads(t, "Campaign reply", "Ann Lee replied to \"Spring\".")), _contactId, c.Id, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- status updates

    private Message Outbound(string? status = "sent", Guid? recipientId = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = _clientId, ConversationId = Guid.NewGuid(), ContactId = _contactId, Direction = MessageDirection.Outbound,
        Channel = "whatsapp", ExternalMessageId = "ext1", DeliveryStatus = status, CampaignRecipientId = recipientId
    };

    private IngestMessageRequest Status(string status, string? code = null, string? reason = null) =>
        new(_clientId, Guid.NewGuid(), _contactId, IngestEventType.StatusUpdate, "whatsapp", null, "ext1", status, null, null, null, null, code, reason);

    private Message FoundMessage(Message m)
    {
        _messages.Setup(r => r.FindByExternalIdAsync(_clientId, "whatsapp", "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(m);
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
        _notifier.Verify(n => n.MessageStatusUpdatedAsync(_clientId, m.ConversationId, m.Id, status, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task First_failure_stores_the_reason_and_notifies_members_once()
    {
        _contacts.Setup(l => l.GetFullNameAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync("Ann Lee");
        var m = FoundMessage(Outbound("sent"));
        await Sut().IngestAsync(Status("failed", "131026", "Undeliverable"));
        Assert.Equal(("failed", "131026", "Undeliverable"), (m.DeliveryStatus, m.FailureCode, m.FailureReason));
        Assert.NotNull(m.FailedAt);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.OutboundMessageFailed, It.Is<NotificationText>(t => TextKit.Reads(t, "Message failed to send", "Message failed to send to Ann Lee.")), _contactId, m.ConversationId, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        await Sut().IngestAsync(Status("sent")); // not a repeat of failed; but a second failure must not notify again
        m.DeliveryStatus = "sent";
        await Sut().IngestAsync(Status("failed"));
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.OutboundMessageFailed, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
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

    // ---------------------------------------------------------------- message trace

    private List<(string Step, string Outcome, TraceIds Ids, string Detail)> RecordSteps()
    {
        var steps = new List<(string, string, TraceIds, string)>();
        _tracer.Setup(t => t.Step(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()))
            .Callback<string, string, TraceIds, double?, object?>((s, o, ids, _, d) => steps.Add((s, o, ids, System.Text.Json.JsonSerializer.Serialize(d))));
        return steps;
    }

    [Fact]
    public async Task A_member_send_records_its_steps_and_stamps_the_message_with_the_trace()
    {
        var c = Conv();
        var steps = RecordSteps();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        using var activity = new System.Diagnostics.Activity("test").Start();

        await Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello"));

        Assert.Equal([TraceSteps.SendRequested, TraceSteps.SendProviderAccepted], steps.Select(s => s.Step));
        Assert.Equal((_clientId, c.Id, saved!.Id, ConversationChannel.WhatsApp), (steps[0].Ids.ClientId!.Value, steps[0].Ids.ConversationId!.Value, steps[0].Ids.MessageId!.Value, steps[0].Ids.Channel));
        Assert.Contains("\"kind\":\"member\"", steps[0].Detail);
        Assert.Equal("wamid.sent", steps[1].Ids.ExternalMessageId);
        Assert.Equal(activity.TraceId.ToHexString(), saved.TraceId);
    }

    [Fact]
    public async Task A_refused_or_blocked_send_records_why()
    {
        var c = Conv();
        var steps = RecordSteps();
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("blocked by Meta"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello")));
        Assert.Equal((TraceSteps.SendProviderFailed, TraceOutcomes.Failed), (steps[^1].Step, steps[^1].Outcome));
        Assert.Contains("blocked by Meta", steps[^1].Detail);

        steps.Clear();
        _entitlements.Setup(e => e.EnsureCanSendMessagesAsync(_clientId, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("messages", "No active plan"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SendAiReplyAsync(_clientId, c.Id, "hi"));
        Assert.Equal([TraceSteps.SendRequested, TraceSteps.SendBlocked], steps.Select(s => s.Step));
        Assert.Contains("ai", steps[0].Detail);

        steps.Clear();
        var human = Conv(ConversationMode.Human);
        await Assert.ThrowsAsync<ConversationNotInAiModeException>(() => Sut().SendAiReplyAsync(_clientId, human.Id, "hi"));
        Assert.Equal((TraceSteps.SendRejectedMode, TraceOutcomes.Skipped), (steps.Single().Step, steps.Single().Outcome));
    }

    [Fact]
    public async Task A_template_send_records_the_template_and_campaign_kind()
    {
        var c = Conv();
        var t = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = _clientId, Name = "promo", Language = "en", Body = "Hi", Status = WhatsAppTemplateStatus.Approved, Category = "MARKETING" };
        _templates.Setup(r => r.GetReadOnlyAsync(_clientId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        var steps = RecordSteps();
        await Sut().SendCampaignTemplateAsync(_clientId, c.Id, t.Id, [], Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal([TraceSteps.SendRequested, TraceSteps.SendProviderAccepted], steps.Select(s => s.Step));
        Assert.Contains("\"kind\":\"campaign\"", steps[0].Detail);
        Assert.Contains("promo", steps[0].Detail);
    }

    [Fact]
    public async Task An_ai_eligible_customer_message_records_saved_and_inbox_and_no_bot_skip()
    {
        var c = Conv();
        var steps = RecordSteps();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        using var activity = new System.Diagnostics.Activity("test").Start();

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.Equal([TraceSteps.MessageSaved, TraceSteps.InboxNotified], steps.Select(s => s.Step));
        Assert.Equal((saved!.Id, "ext1"), (steps[0].Ids.MessageId!.Value, steps[0].Ids.ExternalMessageId));
        Assert.Equal(activity.TraceId.ToHexString(), saved.TraceId);
    }

    [Fact]
    public async Task A_customer_message_the_bot_will_not_see_records_the_reason()
    {
        var steps = RecordSteps();
        await Sut().IngestAsync(Ingest(Conv(ConversationMode.Human).Id, IngestEventType.CustomerMessage));
        Assert.Contains("conversation_in_human_mode", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);

        steps.Clear();
        await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "e2", messageType: "image"));
        Assert.Contains("media_message", steps.Single(s => s.Step == TraceSteps.ConversationModeChanged).Detail); // QA t7-04
        Assert.Contains("media_message", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);

        steps.Clear();
        await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "e2b", messageType: "reaction"));
        Assert.Contains("message_type_not_supported", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);
        Assert.DoesNotContain(steps, s => s.Step == TraceSteps.ConversationModeChanged); // a reaction needs nobody

        steps.Clear();
        InboxSettings(planIncludesBot: false, reopenedConversations: true);
        await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "e3"));
        Assert.Contains("plan_without_ai_assistant", steps.Single(s => s.Step == TraceSteps.ConversationModeChanged).Detail);
        Assert.Contains("plan_without_ai_assistant", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);

        steps.Clear();
        var closed = Conv();
        closed.Status = ConversationStatus.Closed;
        InboxSettings(planIncludesBot: true, reopenedConversations: false);
        await Sut().IngestAsync(Ingest(closed.Id, IngestEventType.CustomerMessage, externalId: "e4"));
        Assert.Contains(steps, s => s.Step == TraceSteps.ConversationReopened);
        Assert.Contains("reopened_to_human", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);

        steps.Clear();
        await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.BusinessAppEcho, externalId: "e5"));
        Assert.Contains("member_replied_from_business_app", steps.Single(s => s.Step == TraceSteps.ConversationModeChanged).Detail);
        Assert.DoesNotContain(steps, s => s.Step == TraceSteps.BotSkipped); // not a customer message
    }

    // ---------------------------------------------------------------- who answers (QA t7-02, t7-04)

    private void Decision(AccountAiDecision decision) =>
        _inboxSettings.Setup(a => a.ResolveAsync(_clientId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(decision);

    private void VerifyHandoffNotice(Guid conversationId, string reasonKey, Func<Times> times) =>
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.Handoff,
            It.Is<NotificationText>(t => t.MessageKey == "notifications.handoff.messageAutomatic" && ((TextArg)t.Args[1]!).Key == reasonKey),
            _contactId, conversationId, null, null, $"/inbox?conversationId={conversationId}", It.IsAny<CancellationToken>()), times);

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    [InlineData("video")]
    [InlineData("document")]
    [InlineData("sticker")]
    [InlineData("location")]
    [InlineData("contacts")]
    [InlineData("unsupported")]
    public async Task Media_in_ai_mode_goes_to_the_team_with_a_notice_and_the_bots_handoff_message(string messageType)
    {
        // Decided 2026-11-05 (QA t7-04): anything but text goes to the team, caption or not; the bot reads text only for now.
        var botId = Guid.NewGuid();
        Decision(new AccountAiDecision(Guid.NewGuid(), botId, null, true, true, "A team member will reply shortly."));
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contact.StageKey = PipelineKit.New;
        var c = Conv();

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, messageType: messageType) with { Content = "How much would this cost?" });

        Assert.False(r.AiEligible);
        Assert.Equal(ConversationMode.Human, c.Mode);
        Assert.Equal(PipelineKit.NeedsHuman, _contact.StageKey);
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, _contactId, c.Id, null, MessageOrigin.System, "{\"reason\":\"media_message\"}"), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);
        VerifyHandoffNotice(c.Id, "notifications.handoff.reason.mediaMessage", Times.Once);
        _sender.Verify(s => s.SendTextAsync(c, "A team member will reply shortly.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Media_the_team_already_has_and_a_reaction_change_nothing()
    {
        var human = Conv(ConversationMode.Human);
        await Sut().IngestAsync(Ingest(human.Id, IngestEventType.CustomerMessage, externalId: "v1", messageType: "audio"));
        var ai = Conv();
        var r = await Sut().IngestAsync(Ingest(ai.Id, IngestEventType.CustomerMessage, externalId: "r1", messageType: "reaction"));

        Assert.Equal((ConversationMode.Human, ConversationMode.Ai, false), (human.Mode, ai.Mode, r.AiEligible));
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), NotificationType.Handoff, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_switch_to_the_team_because_the_bot_cant_answer_notifies_members_with_why()
    {
        // QA t7-02: an automatic switch (plan without the AI assistant, bot inactive, ...) was silent.
        InboxSettings(planIncludesBot: false, reopenedConversations: true);
        var c = Conv();

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.Equal(ConversationMode.Human, c.Mode);
        VerifyHandoffNotice(c.Id, "notifications.handoff.reason.planWithoutAiAssistant", Times.Once);
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never); // no bot message
    }

    [Fact]
    public async Task A_conversation_with_the_team_stays_with_it_until_it_was_closed_and_the_contact_writes_again()
    {
        // Decided 2026-11-05 (QA t7-02): handed to the team = the team's until Return to AI, or a fresh start after it was closed.
        var open = Conv(ConversationMode.Human);
        var r = await Sut().IngestAsync(Ingest(open.Id, IngestEventType.CustomerMessage, externalId: "o1"));
        Assert.Equal((ConversationMode.Human, false), (open.Mode, r.AiEligible));

        var closed = Conv(ConversationMode.Human);
        closed.Status = ConversationStatus.Closed;
        r = await Sut().IngestAsync(Ingest(closed.Id, IngestEventType.CustomerMessage, externalId: "c1"));
        Assert.Equal((ConversationMode.Ai, ConversationStatus.Active, true), (closed.Mode, closed.Status, r.AiEligible));
        Assert.Equal(MessageMetadata.ReturnedToAiMarker, Assert.Single(closed.Messages).MetadataJson);
        _events.Verify(e => e.Log(_clientId, EventTypes.ReturnedToAi, _contactId, closed.Id, null, MessageOrigin.System, "{\"reason\":\"reopened\"}"), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, closed.Id, ConversationMode.Ai, It.IsAny<CancellationToken>()), Times.Once);

        // ... but not when reopened conversations go to the team, or the bot can't answer, or it is a photo.
        InboxSettings(planIncludesBot: true, reopenedConversations: false);
        var toTeam = Conv(ConversationMode.Human);
        toTeam.Status = ConversationStatus.Closed;
        await Sut().IngestAsync(Ingest(toTeam.Id, IngestEventType.CustomerMessage, externalId: "c2"));
        InboxSettings(planIncludesBot: false, reopenedConversations: true);
        var noBot = Conv(ConversationMode.Human);
        noBot.Status = ConversationStatus.Closed;
        await Sut().IngestAsync(Ingest(noBot.Id, IngestEventType.CustomerMessage, externalId: "c3"));
        InboxSettings(planIncludesBot: true, reopenedConversations: true);
        var photo = Conv(ConversationMode.Human);
        photo.Status = ConversationStatus.Closed;
        await Sut().IngestAsync(Ingest(photo.Id, IngestEventType.CustomerMessage, externalId: "c4", messageType: "image"));
        Assert.All(new[] { toTeam, noBot, photo }, c => Assert.Equal((ConversationMode.Human, ConversationStatus.Active), (c.Mode, c.Status)));
    }

    [Fact]
    public async Task A_conversation_that_started_with_the_team_says_why_in_its_trace()
    {
        var steps = RecordSteps();
        Decision(new AccountAiDecision(Guid.NewGuid(), Guid.NewGuid(), null, NewConversations: false, ReopenedConversations: true));
        await Sut().IngestAsync(Ingest(Conv(ConversationMode.Human).Id, IngestEventType.CustomerMessage));
        Assert.Contains("new_conversations_off", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);

        steps.Clear();
        InboxSettings(planIncludesBot: false, reopenedConversations: true);
        await Sut().IngestAsync(Ingest(Conv(ConversationMode.Human).Id, IngestEventType.CustomerMessage, externalId: "e2"));
        Assert.Contains("\"startedWithTeam\":\"plan_without_ai_assistant\"", steps.Single(s => s.Step == TraceSteps.BotSkipped).Detail);
    }

    // ---------------------------------------------------------------- service window (QA t8-05)

    [Fact]
    public async Task The_service_window_runs_from_when_the_customer_wrote_and_never_moves_back()
    {
        var now = DateTimeOffset.UtcNow;
        var late = Conv(windowOpen: false);
        await Sut().IngestAsync(Ingest(late.Id, IngestEventType.CustomerMessage, externalId: "w1") with { ReceivedAt = now.AddHours(-30) });
        Assert.Equal(now.AddHours(-30), late.LastCustomerMessageAt);
        Assert.Equal(now.AddHours(-6), late.ServiceWindowExpiresAt);
        Assert.False(late.IsServiceWindowOpen(now)); // a 30-hour-old message doesn't reopen it

        var open = Conv(); // open for 5 more hours
        var expires = open.ServiceWindowExpiresAt;
        open.LastCustomerMessageAt = now.AddHours(-19);
        await Sut().IngestAsync(Ingest(open.Id, IngestEventType.CustomerMessage, externalId: "w2") with { ReceivedAt = now.AddHours(-20) });
        Assert.Equal((expires, now.AddHours(-19)), (open.ServiceWindowExpiresAt, open.LastCustomerMessageAt)); // an older message moves nothing back

        var future = Conv(windowOpen: false);
        await Sut().IngestAsync(Ingest(future.Id, IngestEventType.CustomerMessage, externalId: "w3") with { ReceivedAt = now.AddHours(2) });
        Assert.InRange(future.ServiceWindowExpiresAt!.Value, now.AddHours(24), DateTimeOffset.UtcNow.AddHours(24)); // never later than now + 24h
    }

    // ---------------------------------------------------------------- tapped buttons (QA t8-08)

    [Fact]
    public async Task A_tapped_stop_button_opts_out_and_a_tapped_reply_reaches_the_bot()
    {
        _optOut.Setup(o => o.MatchKeyword(It.IsAny<string?>())).Returns((string? text) =>
            text?.Trim().ToLowerInvariant() switch { "stop" => true, "start" => false, _ => (bool?)null });
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);

        var promo = await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "b1", messageType: "button")
            with { Content = "Stop promotions", MetadataJson = """{"payload":"unsubscribe","text":"Stop promotions"}""" });
        var byId = await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "b2", messageType: "interactive")
            with { Content = "No more offers", MetadataJson = """{"type":"button_reply","button_reply":{"id":"STOP","title":"No more offers"}}""" });
        var confirm = await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "b3", messageType: "button")
            with { Content = "Yes, confirm", MetadataJson = """{"payload":"confirm","text":"Yes, confirm"}""" });
        var startWord = await Sut().IngestAsync(Ingest(Conv().Id, IngestEventType.CustomerMessage, externalId: "b4", messageType: "button")
            with { Content = "Start booking", MetadataJson = """{"payload":"book"}""" });

        _optOut.Verify(o => o.Apply(_contact, true, It.IsAny<string>(), It.IsAny<Guid?>()), Times.Exactly(2));
        _optOut.Verify(o => o.Apply(_contact, false, It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never); // "Start ..." on a button isn't a start word
        Assert.Equal((false, false, true, true), (promo.AiEligible, byId.AiEligible, confirm.AiEligible, startWord.AiEligible));
    }

    // ---------------------------------------------------------------- bad ingest input (QA t8-06)

    [Theory]
    [InlineData("messageType")]
    [InlineData("externalMessageId")]
    [InlineData("metadataJson")]
    [InlineData("deliveryStatus")]
    public async Task Ingest_values_that_dont_fit_are_a_400_naming_the_field_and_nothing_is_saved(string field)
    {
        var c = Conv();
        var request = Ingest(c.Id, IngestEventType.CustomerMessage);
        request = field switch
        {
            "messageType" => request with { MessageType = new string('x', 40) },
            "externalMessageId" => request with { ExternalMessageId = new string('x', 306) },
            "deliveryStatus" => request with { DeliveryStatus = new string('x', 31) },
            _ => request with { MetadataJson = "{not json" },
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(request));

        Assert.Equal(field == "metadataJson" ? RequestChecks.NotJsonKey : RequestChecks.TooLongKey, ex.Message);
        Assert.Equal(field, Assert.IsType<TextArg>(TextError.ArgsOf(ex)[0]).StoredValue);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
    }

    // ---------------------------------------------------------------- AI reply billing (ai_reply)

    private readonly MessageBillingHold _aiHold = new(Guid.Empty, "ai:reply:x", BillingSettlementTrigger.OnProviderAccepted);

    [Fact]
    public async Task AiReply_reserves_ai_reply_before_sending_and_settles_both_after()
    {
        var c = Conv();
        var order = new List<string>();
        Guid? reservedFor = null;
        _billing.Setup(b => b.ReserveAiReplyAsync(_clientId, c.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, Guid, CancellationToken>((_, _, id, _) => { reservedFor = id; order.Add("ai_reply"); }).ReturnsAsync(_aiHold);
        _billing.Setup(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("channel")).ReturnsAsync(_hold);
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("send")).ReturnsAsync("wamid.sent");

        var sent = await Sut().SendAiReplyAsync(_clientId, c.Id, "Sure");

        Assert.Equal(["ai_reply", "channel", "send"], order);
        Assert.Equal(sent!.Id, reservedFor); // keyed on the message id it is saved under
        _billing.Verify(b => b.SentAsync(_aiHold, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _conversationService.Verify(s => s.HandOverUnpaidAiReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(UsageFailureReason.InsufficientFunds)]
    [InlineData(UsageFailureReason.RateNotFound)]
    public async Task An_ai_reply_the_client_cant_pay_is_not_sent_and_goes_to_the_team(string reason)
    {
        var c = Conv();
        var steps = RecordSteps();
        _billing.Setup(b => b.ReserveAiReplyAsync(_clientId, c.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(BillingDeniedException.For(reason));

        var ex = await Assert.ThrowsAsync<AiReplyRefusedException>(() => Sut().SendAiReplyAsync(_clientId, c.Id, "hi"));

        Assert.Equal(reason, ex.Reason);
        _conversationService.Verify(s => s.HandOverUnpaidAiReplyAsync(_clientId, c.Id, reason, It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _billing.Verify(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        var blocked = steps.Single(s => s.Step == TraceSteps.SendBlocked);
        Assert.Contains(reason, blocked.Detail);
        Assert.Contains("ai_reply_" + reason, steps.Single(s => s.Step == TraceSteps.ConversationModeChanged).Detail);
    }

    [Fact]
    public async Task A_failed_ai_send_or_channel_refusal_returns_the_ai_reply_hold()
    {
        var c = Conv();
        _billing.Setup(b => b.ReserveAiReplyAsync(_clientId, c.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_aiHold);
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("x"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendAiReplyAsync(_clientId, c.Id, "hi"));
        _billing.Verify(b => b.SendFailedAsync(_aiHold, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);

        // The channel's own message usage refused (e.g. an empty wallet for a paid channel): the ai_reply hold goes back too.
        _billing.Setup(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(BillingDeniedException.For(UsageFailureReason.InsufficientFunds));
        await Assert.ThrowsAsync<BillingDeniedException>(() => Sut().SendAiReplyAsync(_clientId, c.Id, "hi"));
        _billing.Verify(b => b.SendFailedAsync(_aiHold, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _billing.Verify(b => b.SentAsync(It.IsAny<MessageBillingHold?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_member_send_never_reserves_an_ai_reply()
    {
        var c = Conv();
        await Sut().SendAsync(_clientId, c.Id, new SendMessageRequest("hello"));
        _billing.Verify(b => b.ReserveAiReplyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_ingest_naming_another_contact_than_the_conversations_is_refused_and_changes_nothing()
    {
        // QA t8-03: a STOP into client A's conversation with client B's contactId opted B's contact out.
        var c = Conv();
        var request = Ingest(c.Id, IngestEventType.CustomerMessage) with { ContactId = Guid.NewGuid(), Content = "STOP" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(request));

        Assert.Equal("inbox.errors.contactNotInConversation", ex.Message);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        _optOut.Verify(o => o.Apply(It.IsAny<Contact>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_ingest_without_a_contact_id_uses_the_conversations_contact_read_within_the_client()
    {
        var c = Conv();
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { ContactId = Guid.Empty });

        Assert.Equal(_contactId, saved!.ContactId);
        _contacts.Verify(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>()), Times.Once);
        _contacts.Verify(l => l.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    // QA t10-03: the first delivery saved the message, then failed before the AI ran; every retry was ignored as a duplicate.
    [InlineData(300, ConversationMode.Ai, "text", "when are you open?", false, true)]
    [InlineData(30, ConversationMode.Ai, "text", "when are you open?", false, false)]     // too young: a run may still be going
    [InlineData(300, ConversationMode.Human, "text", "when are you open?", false, false)]  // the team has it
    [InlineData(300, ConversationMode.Ai, "image", null, false, false)]                   // never went to the AI anyway
    [InlineData(300, ConversationMode.Ai, "text", "STOP", false, false)]                  // a stop word isn't for the AI
    [InlineData(300, ConversationMode.Ai, "text", "when are you open?", true, false)]     // something came after it
    public async Task A_repeat_of_a_customer_message_nobody_answered_asks_the_ai_again(int ageSeconds, string mode, string type, string? content,
        bool laterMessages, bool expected)
    {
        var c = Conv(mode);
        var existing = new Message
        {
            Id = Guid.NewGuid(), ClientId = _clientId, ConversationId = c.Id, ContactId = _contactId, Direction = MessageDirection.Inbound,
            SenderType = MessageSenderType.Contact, Channel = ConversationChannel.WhatsApp, MessageType = type, Content = content,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds)
        };
        _messages.Setup(m => m.FindByExternalIdAsync(_clientId, ConversationChannel.WhatsApp, "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _messages.Setup(m => m.HasMessagesSinceAsync(c.Id, existing.CreatedAt, existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(laterMessages);
        _optOut.Setup(o => o.MatchKeyword("STOP")).Returns(true);

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.Equal((true, expected), (r.Deduplicated, r.AiEligible));
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
    }

    [Fact]
    public async Task A_duplicate_delivery_records_a_skip()
    {
        var c = Conv();
        var existing = new Message { Id = Guid.NewGuid(), ClientId = _clientId, ConversationId = c.Id, ContactId = _contactId, Direction = MessageDirection.Inbound, SenderType = MessageSenderType.Contact, Channel = ConversationChannel.WhatsApp };
        _messages.Setup(m => m.FindByExternalIdAsync(_clientId, ConversationChannel.WhatsApp, "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var steps = RecordSteps();
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));
        var step = steps.Single();
        Assert.Equal((TraceSteps.MessageDeduplicated, TraceOutcomes.Skipped, existing.Id), (step.Step, step.Outcome, step.Ids.MessageId!.Value));
    }

    // ---- opt-out (Milestone C S3) ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_stop_word_opts_the_contact_out_skips_the_ai_and_sends_one_system_confirmation()
    {
        var c = Conv(ConversationMode.Ai);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _optOut.Setup(o => o.MatchKeyword("STOP")).Returns(true);
        _optOut.Setup(o => o.ConfirmationTextAsync(_clientId, _contact, true, It.IsAny<CancellationToken>())).ReturnsAsync("You're unsubscribed.");
        var saved = new List<Message>();
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(saved.Add);

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { Content = "STOP" });

        Assert.False(r.AiEligible);
        _optOut.Verify(o => o.Apply(_contact, true, MessageOrigin.WhatsAppCustomer, c.Id), Times.Once);
        var reply = saved.Single(m => m.Direction == MessageDirection.Outbound);
        Assert.Equal((MessageSenderType.System, MessageOrigin.System, "You're unsubscribed."), (reply.SenderType, reply.Origin, reply.Content));
        _sender.Verify(s => s.SendTextAsync(c, "You're unsubscribed.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ConversationMode.Ai, c.Mode); // an automatic message never takes the conversation over
    }

    [Fact]
    public async Task A_stop_that_changes_nothing_is_not_confirmed_again()
    {
        // QA t3-inbox-03: a second STOP got a second "you're unsubscribed".
        var c = Conv(ConversationMode.Ai);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _optOut.Setup(o => o.MatchKeyword("STOP")).Returns(true);
        _optOut.Setup(o => o.Apply(_contact, true, It.IsAny<string>(), It.IsAny<Guid?>())).Returns(false);

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { Content = "STOP" });

        Assert.False(r.AiEligible); // still not a question for the AI
        _optOut.Verify(o => o.ConfirmationTextAsync(It.IsAny<Guid>(), It.IsAny<Contact>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("/start")]
    [InlineData("/start campaign_42")]
    [InlineData("/START")]
    public async Task Telegrams_start_opens_the_chat_it_is_not_a_subscribe_again(string text)
    {
        // QA t3-inbox-03: every new Telegram contact got "You're subscribed again" and no greeting.
        var c = Conv(ConversationMode.Ai);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _optOut.Setup(o => o.MatchKeyword(It.IsAny<string?>())).Returns(false);
        AutoReply(AutoReplyChoice.Send("Welcome! How can we help?"));

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, messageType: "command", newContact: true) with { Content = text });

        Assert.True(r.AiEligible); // the bot greets
        _optOut.Verify(o => o.MatchKeyword(It.IsAny<string?>()), Times.Never);
        _optOut.Verify(o => o.Apply(It.IsAny<Contact>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        _autoReplies.Verify(a => a.ChooseAsync(_clientId, It.IsAny<string>(), AutoReplyTrigger.NewConversation, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_stop_command_still_opts_the_contact_out()
    {
        var c = Conv(ConversationMode.Ai);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _optOut.Setup(o => o.MatchKeyword("/stop")).Returns(true);
        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, messageType: "command") with { Content = "/stop" });
        Assert.False(r.AiEligible);
        _optOut.Verify(o => o.Apply(_contact, true, It.IsAny<string>(), c.Id), Times.Once);
    }

    [Fact]
    public async Task A_failed_confirmation_never_fails_the_inbound_message()
    {
        var c = Conv(ConversationMode.Human);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _optOut.Setup(o => o.MatchKeyword(It.IsAny<string?>())).Returns(false);
        _optOut.Setup(o => o.ConfirmationTextAsync(_clientId, _contact, false, It.IsAny<CancellationToken>())).ReturnsAsync("Welcome back.");
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), "Welcome back.", It.IsAny<CancellationToken>())).ThrowsAsync(new ChannelSendException("window closed"));

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { Content = "start" });

        Assert.True(r.Found);
        _optOut.Verify(o => o.Apply(_contact, false, It.IsAny<string>(), c.Id), Times.Once);
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ordinary_messages_and_echoes_never_touch_the_opt_out()
    {
        var c = Conv(ConversationMode.Ai);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));
        Assert.True(r.AiEligible);
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.BusinessAppEcho, externalId: "ext2") with { Content = "stop" });
        _optOut.Verify(o => o.MatchKeyword("stop"), Times.Never);
        _optOut.Verify(o => o.Apply(It.IsAny<Contact>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage, externalId: "ext3", messageType: "image") with { Content = "stop" });
        _optOut.Verify(o => o.MatchKeyword("stop"), Times.Never);
    }

    [Fact]
    public async Task No_template_goes_to_an_opted_out_contact()
    {
        var c = Conv();
        var t = Template();
        Setup(t);
        _contact.OptedOutAt = DateTimeOffset.UtcNow;
        _contact.MarketingOptIn = false;
        Assert.Equal("inbox.errors.contactOptedOut",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendTemplateAsync(_clientId, c.Id, new SendTemplateMessageRequest(t.Id, ["Ann", "20%"])))).Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendCampaignTemplateAsync(_clientId, c.Id, t.Id, ["Ann", "20%"], Guid.NewGuid(), Guid.NewGuid()));
        _whatsApp.Verify(w => w.SendTemplateMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _billing.Verify(b => b.ReserveOutboundAsync(It.IsAny<OutboundMessageBillingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_system_text_needs_content_and_a_conversation_of_the_client()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendSystemTextAsync(_clientId, Guid.NewGuid(), " "));
        Assert.Null(await Sut().SendSystemTextAsync(_clientId, Guid.NewGuid(), "Hi"));
    }

    // ---------------------------------------------------------------- auto-replies

    private void VerifyChoose(string trigger, bool aiAnswers, Func<Times> times) =>
        _autoReplies.Verify(a => a.ChooseAsync(_clientId, ConversationChannel.WhatsApp, trigger, aiAnswers, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task A_contacts_first_message_gets_the_new_conversation_auto_reply_as_an_automatic_message()
    {
        var c = Conv(ConversationMode.Human);
        AutoReply(AutoReplyChoice.Send("Thanks, we'll answer soon."));
        var saved = new List<Message>();
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(saved.Add);

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.True(r.Found);
        VerifyChoose(AutoReplyTrigger.NewConversation, false, Times.Once);
        _sender.Verify(s => s.SendTextAsync(c, "Thanks, we'll answer soon.", It.IsAny<CancellationToken>()), Times.Once);
        var reply = Assert.Single(saved, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal((MessageSenderType.System, MessageOrigin.System, MessageMetadata.Automatic), (reply.SenderType, reply.Origin, reply.MetadataJson));
        Assert.Equal(ConversationMode.Human, c.Mode);
        _events.Verify(e => e.Log(_clientId, EventTypes.AutoReplySent, _contactId, c.Id, null, MessageOrigin.System, "{\"trigger\":\"new\"}"), Times.Once);
        _billing.Verify(b => b.SentAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_auto_reply_is_told_when_the_ai_bot_answers_the_message()
    {
        var c = Conv(ConversationMode.Ai);
        AutoReply(AutoReplyChoice.Skip(AutoReplyRules.SkipAiAnswers));

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.True(r.AiEligible);
        VerifyChoose(AutoReplyTrigger.NewConversation, true, Times.Once);
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tracer.Verify(t => t.Step(TraceSteps.AutoReplySkipped, TraceOutcomes.Skipped, It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task A_message_that_reopens_a_closed_conversation_gets_the_reopened_auto_reply()
    {
        InboxSettings(planIncludesBot: true, reopenedConversations: false); // reopened conversations go to the team: it stays human
        var c = Conv(ConversationMode.Human);
        c.LastCustomerMessageAt = DateTimeOffset.UtcNow.AddDays(-3);
        c.Status = ConversationStatus.Closed;
        AutoReply(AutoReplyChoice.Send("Welcome back!"));

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        VerifyChoose(AutoReplyTrigger.Reopened, false, Times.Once);
        VerifyChoose(AutoReplyTrigger.NewConversation, false, Times.Never);
        _sender.Verify(s => s.SendTextAsync(c, "Welcome back!", It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.AutoReplySent, _contactId, c.Id, null, MessageOrigin.System, "{\"trigger\":\"reopened\"}"), Times.Once);
    }

    [Fact]
    public async Task A_later_message_in_an_open_conversation_gets_no_auto_reply()
    {
        var c = Conv(ConversationMode.Human);
        c.LastCustomerMessageAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        AutoReply(AutoReplyChoice.Send("Hi"));

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        _autoReplies.Verify(a => a.ChooseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _sender.Verify(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(IngestEventType.BusinessAppEcho)]
    [InlineData(IngestEventType.AiMessage)]
    public async Task Only_a_contacts_message_can_trigger_an_auto_reply(string eventType)
    {
        var c = Conv(ConversationMode.Human);
        c.Status = ConversationStatus.Closed;
        AutoReply(AutoReplyChoice.Send("Hi"));

        await Sut().IngestAsync(Ingest(c.Id, eventType));

        _autoReplies.Verify(a => a.ChooseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_stop_word_gets_its_confirmation_and_no_auto_reply()
    {
        var c = Conv(ConversationMode.Human);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _optOut.Setup(o => o.MatchKeyword(It.IsAny<string?>())).Returns(true);
        AutoReply(AutoReplyChoice.Send("Hi"));

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        _autoReplies.Verify(a => a.ChooseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_auto_reply_that_cannot_be_sent_never_fails_the_saved_message()
    {
        var c = Conv(ConversationMode.Human);
        AutoReply(AutoReplyChoice.Send("Hi"));
        _sender.Setup(s => s.SendTextAsync(It.IsAny<Conversation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refused"));

        var r = await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage));

        Assert.True(r.Found);
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _tracer.Verify(t => t.Step(TraceSteps.SendBlocked, TraceOutcomes.Failed, It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()), Times.Once);
    }
    // ---------------------------------------------------------------- QA t8-07: inconsistent ingest data

    [Theory]
    [InlineData(ConversationChannel.Telegram)]
    [InlineData("pigeon")]
    public async Task A_message_on_another_channel_than_its_conversation_is_refused(string channel)
    {
        var c = Conv();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { Channel = channel }));
        Assert.Equal(MessageService.ChannelMismatchKey, ex.Message);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
    }

    [Fact]
    public async Task A_message_without_a_channel_takes_its_conversations()
    {
        var c = Conv(channel: ConversationChannel.Telegram);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { Channel = "" });
        Assert.Equal(ConversationChannel.Telegram, saved!.Channel);
    }

    [Fact]
    public async Task Times_in_the_future_are_stored_as_now_and_an_inbound_message_carries_no_delivery_status()
    {
        var c = Conv();
        var saved = new List<Message>();
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(saved.Add);
        var future = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.CustomerMessage) with { ReceivedAt = future, DeliveryStatus = "read" });
        await Sut().IngestAsync(Ingest(c.Id, IngestEventType.AiMessage, externalId: "ext2") with { SentAt = future });

        Assert.True(saved[0].ReceivedAt <= DateTimeOffset.UtcNow);
        Assert.Null(saved[0].DeliveryStatus);
        Assert.True(saved[1].SentAt <= DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("banana")]
    public async Task A_status_update_needs_a_real_status_and_never_clears_the_one_stored(string? status)
    {
        var m = FoundMessage(Outbound("read"));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(Status("read") with { DeliveryStatus = status }));
        Assert.Equal(MessageService.InvalidDeliveryStatusKey, ex.Message);
        Assert.Equal("read", m.DeliveryStatus);
    }

    [Fact]
    public async Task A_status_update_for_a_customers_message_is_refused()
    {
        var inbound = Outbound(null);
        inbound.Direction = MessageDirection.Inbound;
        FoundMessage(inbound);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().IngestAsync(Status("failed")));
        Assert.Equal(MessageService.StatusOnInboundKey, ex.Message);
        Assert.Null(inbound.FailedAt);
    }

    [Fact]
    public async Task A_status_time_in_the_future_is_stored_as_now_and_the_status_in_lower_case()
    {
        var m = FoundMessage(Outbound("sent"));
        await Sut().IngestAsync(Status("DELIVERED") with { OccurredAt = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        Assert.Equal("delivered", m.DeliveryStatus);
        Assert.True(m.DeliveredAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void The_new_contact_flag_is_never_read_from_a_request_body()
    {
        var json = $"{{\"clientId\":\"{_clientId}\",\"conversationId\":\"{Guid.NewGuid()}\",\"contactId\":\"{_contactId}\",\"eventType\":\"customer_message\",\"channel\":\"whatsapp\",\"contactWasNewlyCreated\":true}}";
        var request = System.Text.Json.JsonSerializer.Deserialize<IngestMessageRequest>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.False(request!.ContactWasNewlyCreated);
    }

    // QA t8-11: a campaign tells the members once for the whole campaign, not once per recipient.
    [Fact]
    public async Task A_failed_campaign_send_releases_the_hold_without_a_notification_per_recipient()
    {
        var c = Conv();
        var t = Template();
        Setup(t);
        _whatsApp.Setup(w => w.SendTemplateMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("rejected"));
        await Assert.ThrowsAsync<ChannelSendException>(() => Sut().SendCampaignTemplateAsync(_clientId, c.Id, t.Id, [], Guid.NewGuid(), Guid.NewGuid()));
        _billing.Verify(b => b.SendFailedAsync(_hold, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), NotificationType.OutboundMessageFailed, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

}
