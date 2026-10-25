using Omni.App.Business.Services.Inbox;

namespace Omni.App.Tests.Services;

public class ConversationServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _contactId = Guid.NewGuid();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IInboxSettingsService> _inboxSettings = new();
    private readonly Mock<IBusinessTypeService> _businessTypes = PipelineKit.Types();

    public ConversationServiceTests()
    {
        InboxSettings(planIncludesBot: true, newConversations: true, reopenedConversations: true);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IUnitOfWorkTransaction>());
    }

    private ConversationService Sut() => new(_conversations.Object, _messages.Object, _contacts.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object, _inboxSettings.Object, _businessTypes.Object);

    /// <summary>Inbox settings where every account's bot answers, except without the plan's AI assistant or on the channels
    /// in <paramref name="withoutBot"/> (an inactive bot there).</summary>
    private void InboxSettings(bool planIncludesBot, bool newConversations, bool reopenedConversations, params string[] withoutBot)
    {
        AccountAiDecision Decide(string channel) => new(Guid.NewGuid(), Guid.NewGuid(),
            !planIncludesBot ? AiBotBlockReason.PlanWithoutAi : withoutBot.Contains(channel) ? AiBotBlockReason.BotInactive : null,
            newConversations, reopenedConversations);
        _inboxSettings.Setup(a => a.ResolveAsync(_clientId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string channel, CancellationToken _) => Decide(channel));
        _inboxSettings.Setup(a => a.InitialModeAsync(_clientId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string channel, CancellationToken _) => Decide(channel).AnswersNew ? ConversationMode.Ai : ConversationMode.Human);
    }

    private Conversation Conv(string mode = ConversationMode.Ai, string? externalThread = null)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(), ClientId = _clientId, ContactId = _contactId, Channel = ConversationChannel.WhatsApp, Mode = mode,
            Status = ConversationStatus.Active, AiEnabled = mode == ConversationMode.Ai, HumanTakeover = mode == ConversationMode.Human, ExternalThreadId = externalThread
        };
        _conversations.Setup(r => r.GetAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _conversations.Setup(r => r.GetForUpdateAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    [Fact]
    public async Task Create_validates_channel_and_starts_in_ai_mode()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateConversationRequest(_clientId, _contactId, "pigeon", null)));
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(new Contact { Id = _contactId, ClientId = _clientId });
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);
        var r = await Sut().CreateAsync(new CreateConversationRequest(_clientId, _contactId, ConversationChannel.Telegram, "t1"));
        Assert.Equal((ConversationMode.Ai, true, false, ConversationStatus.Active, "t1"), (added!.Mode, added.AiEnabled, added.HumanTakeover, added.Status, added.ExternalThreadId));
        Assert.Equal(added.Id, r.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_for_a_contact_this_client_doesnt_have_is_not_found()
    {
        // Covers both a contact that doesn't exist and another client's contact: the lookup is scoped to the client.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CreateAsync(new CreateConversationRequest(_clientId, Guid.NewGuid(), ConversationChannel.Website, null)));
        _conversations.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateForContact_reuses_an_existing_conversation()
    {
        var c = Conv();
        _conversations.Setup(r => r.FindForContactAsync(_clientId, _contactId, "whatsapp", It.IsAny<CancellationToken>())).ReturnsAsync(c);
        Assert.Equal(c.Id, (await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "whatsapp")).Id);
        _conversations.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateForContact_creates_when_missing()
    {
        await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "whatsapp");
        _conversations.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateForContact_with_thread_fills_a_missing_thread_id_only()
    {
        var blank = Conv();
        _conversations.Setup(r => r.FindForContactAsync(_clientId, _contactId, "telegram", It.IsAny<CancellationToken>())).ReturnsAsync(blank);
        await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "telegram", "chat-1");
        Assert.Equal("chat-1", blank.ExternalThreadId);

        await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "telegram", "chat-2");
        Assert.Equal("chat-1", blank.ExternalThreadId);
    }

    [Fact]
    public async Task GetOrCreateForContact_with_thread_creates_validates_and_yields_to_a_concurrent_winner()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().GetOrCreateForContactAsync(_clientId, _contactId, "pigeon", "x"));

        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);
        Assert.Equal("th", (await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "telegram", "th")) is { } r && added!.ExternalThreadId == "th" ? "th" : "");

        var winner = Conv(externalThread: "th2");
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _conversations.Setup(x => x.FindByExternalThreadAsync(_clientId, "telegram", "th2", It.IsAny<CancellationToken>())).ReturnsAsync(winner);
        Assert.Equal(winner.Id, (await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "telegram", "th2")).Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetOrCreateForContactAsync(_clientId, _contactId, "telegram", "th3"));
    }

    [Fact]
    public async Task GetByIdWithMessages_returns_null_or_both_parts()
    {
        Assert.Null(await Sut().GetByIdWithMessagesAsync(_clientId, Guid.NewGuid()));
        var c = Conv();
        _messages.Setup(m => m.ListForConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message> { new() { Id = Guid.NewGuid(), ConversationId = c.Id, Content = "hi" } });
        var result = await Sut().GetByIdWithMessagesAsync(_clientId, c.Id);
        Assert.Equal(c.Id, result!.Value.Conversation.Id);
        Assert.Equal("hi", Assert.Single(result.Value.Messages).Content);
    }

    [Fact]
    public async Task GetMessages_is_empty_for_a_foreign_conversation()
    {
        Assert.Empty(await Sut().GetMessagesAsync(_clientId, Guid.NewGuid()));
        var c = Conv();
        _conversations.Setup(r => r.ExistsAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _messages.Setup(m => m.ListForConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message> { new() { Id = Guid.NewGuid() } });
        Assert.Single(await Sut().GetMessagesAsync(_clientId, c.Id));
        Assert.True(await Sut().BelongsToClientAsync(_clientId, c.Id));
    }

    // ---------------------------------------------------------------- AddMessage

    private static CreateMessageRequest Msg(string direction, string sender, string channel = "whatsapp", string? type = null, string? origin = null) =>
        new(direction, sender, channel, type, "hello", "ext", false, origin);

    [Fact]
    public async Task AddMessage_validates_direction_and_returns_null_for_unknown_conversation()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddMessageAsync(_clientId, Guid.NewGuid(), Msg("sideways", "contact")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddMessageAsync(_clientId, Guid.NewGuid(), Msg("inbound", "martian")));
        Assert.Null(await Sut().AddMessageAsync(_clientId, Guid.NewGuid(), Msg("inbound", "contact")));
    }

    [Fact]
    public async Task Outbound_message_stamps_conversation_and_moves_a_new_contact_to_contacted()
    {
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = PipelineKit.New };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        var r = await Sut().AddMessageAsync(_clientId, c.Id, Msg("outbound", "member"));

        Assert.Equal(("text", MessageOrigin.Dashboard, "hello"), (saved!.MessageType, saved.Origin, saved.Content));
        Assert.NotNull(saved.SentAt);
        Assert.Null(saved.ReceivedAt);
        Assert.Equal(("outbound", saved.CreatedAt), (c.LastMessageDirection, c.LastMessageAt!.Value));
        Assert.Equal(PipelineKit.Contacted, contact.StageKey);
        Assert.NotNull(contact.LastContactAt);
        Assert.Equal(saved.Id, r!.Id);
    }

    [Theory]
    [InlineData("in_touch", "in_touch")]   // only the entry stage moves on
    [InlineData("visit_set", "visit_set")]
    [InlineData("inquiry", "in_touch")]
    public async Task The_first_reply_moves_only_an_entry_stage_contact_to_the_next_open_stage(string from, string expected)
    {
        _businessTypes.Use(PipelineKit.Custom());
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = from };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        await Sut().AddMessageAsync(_clientId, c.Id, Msg("outbound", "member"));
        Assert.Equal(expected, contact.StageKey);
    }

    [Fact]
    public async Task A_type_with_one_open_stage_keeps_the_contact_on_reply()
    {
        _businessTypes.Use(new ClientPipeline([new("open_only", StageCategory.Open, "Open"), new("b", StageCategory.Booked, "B")]));
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = "open_only" };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        await Sut().AddMessageAsync(_clientId, c.Id, Msg("outbound", "member"));
        Assert.Equal("open_only", contact.StageKey);
    }

    [Fact]
    public async Task Inbound_message_keeps_the_contact_status_and_sets_received_time()
    {
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = PipelineKit.New };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().AddMessageAsync(_clientId, c.Id, Msg("inbound", "contact"));
        Assert.Equal(PipelineKit.New, contact.StageKey);
        Assert.NotNull(saved!.ReceivedAt);
        Assert.Null(saved.SentAt);
        Assert.Equal(MessageOrigin.WhatsAppCustomer, saved.Origin);
    }

    [Theory]
    [InlineData("ai", "whatsapp", MessageOrigin.Ai)]
    [InlineData("member", "whatsapp", MessageOrigin.Dashboard)]
    [InlineData("contact", "whatsapp", MessageOrigin.WhatsAppCustomer)]
    [InlineData("contact", "telegram", MessageOrigin.TelegramCustomer)]
    [InlineData("contact", "instagram", MessageOrigin.System)]
    [InlineData("system", "whatsapp", MessageOrigin.System)]
    public async Task Origin_is_inferred_from_sender_and_channel(string sender, string channel, string expected)
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().AddMessageAsync(_clientId, c.Id, Msg("inbound", sender, channel));
        Assert.Equal(expected, saved!.Origin);
    }

    [Fact]
    public async Task An_explicit_origin_wins_over_inference()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().AddMessageAsync(_clientId, c.Id, Msg("outbound", "member", type: "image", origin: MessageOrigin.Campaign));
        Assert.Equal((MessageOrigin.Campaign, "image"), (saved!.Origin, saved.MessageType));
    }

    // ---------------------------------------------------------------- mode / read / close

    [Fact]
    public async Task MarkRead_notifies_only_when_a_row_changed()
    {
        var id = Guid.NewGuid();
        Assert.False(await Sut().MarkReadAsync(_clientId, id));
        _notifier.Verify(n => n.ConversationUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _conversations.Setup(r => r.MarkReadAsync(_clientId, id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Assert.True(await Sut().MarkReadAsync(_clientId, id));
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clientId, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TakeOver_and_ReturnToAi_switch_mode_log_and_broadcast()
    {
        var c = Conv();
        var human = await Sut().TakeOverAsync(_clientId, c.Id);
        Assert.Equal((ConversationMode.Human, true), (human!.Mode, human.HumanTakeover));
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, null, c.Id, null, "dashboard", "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);

        var ai = await Sut().ReturnToAiAsync(_clientId, c.Id);
        Assert.Equal((ConversationMode.Ai, true), (ai!.Mode, ai.AiEnabled));
        Assert.Contains(c.Messages, m => m.Content == "Returned to AI");
        Assert.Null(await Sut().TakeOverAsync(_clientId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Handoff_notifies_members_once_and_not_when_already_human()
    {
        var c = Conv();
        _contacts.Setup(l => l.GetFullNameAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync("Ann Lee");
        Assert.True((await Sut().HandoffToHumanAsync(_clientId, c.Id, "wants a price"))!.HandedOff);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.Handoff, It.Is<NotificationText>(t => TextKit.Reads(t, "AI handed off to your team", "Ann Lee needs your team's attention: wants a price")),
            _contactId, c.Id, null, null, $"/inbox?conversationId={c.Id}", It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, null, c.Id, null, MessageOrigin.Ai, "{\"reason\":\"wants a price\"}"), Times.Once);

        // The locked row is already human: nothing changes, nothing is notified, and the caller sends no handoff message.
        var again = await Sut().HandoffToHumanAsync(_clientId, c.Id, null);
        Assert.Equal((false, ConversationMode.Human), (again!.HandedOff, again.Conversation.Mode));
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, null, c.Id, null, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UsageFailureReason.InsufficientFunds, "Ann Lee needs your team's attention: the AI assistant didn't reply because your plan's credit and wallet can't cover it. See Settings → Billing.")]
    [InlineData(UsageFailureReason.RateNotFound, "Ann Lee needs your team's attention: the AI assistant didn't reply because AI replies aren't priced for your account yet. Please contact support.")]
    public async Task An_unpaid_ai_reply_hands_the_conversation_to_the_team(string reason, string message)
    {
        _businessTypes.Use(PipelineKit.Custom());
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = "in_touch" };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        _contacts.Setup(l => l.GetFullNameAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync("Ann Lee");

        var result = await Sut().HandOverUnpaidAiReplyAsync(_clientId, c.Id, reason);

        Assert.Equal(ConversationMode.Human, result!.Mode);
        Assert.Equal(ConversationMode.Human, c.Mode);
        Assert.Equal("team", contact.StageKey); // needs-human stage, like the AI's own handoff
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, null, c.Id, null, MessageOrigin.System, $"{{\"reason\":\"ai_reply_{reason}\"}}"), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.Handoff, It.Is<NotificationText>(t => TextKit.Reads(t, "AI handed off to your team", message)),
            _contactId, c.Id, null, null, $"/inbox?conversationId={c.Id}", It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);

        // A second refusal on the (now human) conversation doesn't notify again.
        await Sut().HandOverUnpaidAiReplyAsync(_clientId, c.Id, reason);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(await Sut().HandOverUnpaidAiReplyAsync(_clientId, Guid.NewGuid(), reason));
    }

    [Theory]
    [InlineData("in_touch", "team")]
    [InlineData("expert", "expert")]   // already in a needs-human stage: stays there
    public async Task The_ai_handoff_moves_the_contact_to_the_needs_human_stage(string from, string expected)
    {
        _businessTypes.Use(PipelineKit.Custom());
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = from };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        await Sut().HandoffToHumanAsync(_clientId, c.Id, "medical question");
        Assert.Equal(expected, contact.StageKey);
    }

    [Fact]
    public async Task A_member_taking_over_leaves_the_stage_alone()
    {
        var c = Conv();
        var contact = new Contact { Id = _contactId, StageKey = PipelineKit.Qualified };
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        await Sut().TakeOverAsync(_clientId, c.Id);
        Assert.Equal(PipelineKit.Qualified, contact.StageKey);
    }

    [Fact]
    public async Task Handoff_message_falls_back_when_name_or_reason_is_missing_and_unknown_conversation_is_null()
    {
        var c = Conv();
        await Sut().HandoffToHumanAsync(_clientId, c.Id, " ");
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.Handoff, It.Is<NotificationText>(t => TextKit.Reads(t, null, "A conversation needs your team's attention.")), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(await Sut().HandoffToHumanAsync(_clientId, Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task Close_sets_status_logs_and_broadcasts()
    {
        var c = Conv();
        var r = await Sut().CloseAsync(_clientId, c.Id);
        Assert.Equal(ConversationStatus.Closed, r!.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConversationClosed, null, c.Id, null, "dashboard", "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clientId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(await Sut().CloseAsync(_clientId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Reopen_brings_a_closed_conversation_back_logs_and_broadcasts()
    {
        var c = Conv();
        c.Status = ConversationStatus.Closed;

        var r = await Sut().ReopenAsync(_clientId, c.Id);

        Assert.Equal(ConversationStatus.Active, r!.Status);
        Assert.Equal(ConversationStatus.Active, c.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConversationReopened, null, c.Id, null, "dashboard", "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clientId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reopen_leaves_an_active_conversation_alone_and_returns_null_for_an_unknown_one()
    {
        var c = Conv();

        var r = await Sut().ReopenAsync(_clientId, c.Id);

        Assert.Equal(ConversationStatus.Active, r!.Status);
        _events.Verify(e => e.Log(It.IsAny<Guid>(), EventTypes.ConversationReopened, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        _notifier.Verify(n => n.ConversationUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(await Sut().ReopenAsync(_clientId, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_contact_message_added_through_the_generic_path_reopens_but_a_member_message_does_not()
    {
        var closedForContact = Conv();
        closedForContact.Status = ConversationStatus.Closed;
        await Sut().AddMessageAsync(_clientId, closedForContact.Id, Msg("inbound", "contact"));
        Assert.Equal(ConversationStatus.Active, closedForContact.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConversationReopened, null, closedForContact.Id, null, MessageOrigin.WhatsAppCustomer, "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clientId, closedForContact.Id, It.IsAny<CancellationToken>()), Times.Once);

        var closedByMember = Conv();
        closedByMember.Status = ConversationStatus.Closed;
        await Sut().AddMessageAsync(_clientId, closedByMember.Id, Msg("outbound", "member"));
        Assert.Equal(ConversationStatus.Closed, closedByMember.Status);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clientId, closedByMember.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- AI bot routing

    [Theory]
    [InlineData(true, true, ConversationMode.Ai)]
    [InlineData(true, false, ConversationMode.Human)]
    [InlineData(false, true, ConversationMode.Human)]
    public async Task A_new_conversation_starts_with_the_bot_only_when_plan_and_client_allow_it(bool plan, bool newOn, string expected)
    {
        InboxSettings(plan, newOn, reopenedConversations: true);
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);

        await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "whatsapp");
        Assert.Equal((expected, expected == ConversationMode.Ai, expected == ConversationMode.Human), (added!.Mode, added.AiEnabled, added.HumanTakeover));

        added = null;
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().GetOrCreateForContactAsync(_clientId, _contactId, "telegram", "chat-9");
        Assert.Equal(expected, added!.Mode);
    }

    [Fact]
    public async Task A_new_conversation_on_an_account_without_an_answering_bot_starts_with_the_team()
    {
        InboxSettings(planIncludesBot: true, newConversations: true, reopenedConversations: true, ConversationChannel.Telegram);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(new Contact { Id = _contactId, ClientId = _clientId });
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);

        await Sut().CreateAsync(new CreateConversationRequest(_clientId, _contactId, ConversationChannel.Telegram, null));
        Assert.Equal((ConversationMode.Human, true), (added!.Mode, added.HumanTakeover));

        await Sut().CreateAsync(new CreateConversationRequest(_clientId, _contactId, ConversationChannel.WhatsApp, null));
        Assert.Equal(ConversationMode.Ai, added!.Mode);
    }

    [Fact]
    public async Task Return_to_ai_is_refused_on_an_account_without_an_answering_bot()
    {
        InboxSettings(planIncludesBot: true, newConversations: true, reopenedConversations: true, ConversationChannel.WhatsApp);
        var c = Conv(ConversationMode.Human);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ReturnToAiAsync(_clientId, c.Id));
        Assert.Equal("inbox.errors.noBotOnAccount", ex.Message);
        Assert.Null(await Sut().ReturnToAiAsync(_clientId, Guid.NewGuid()));
        Assert.Equal(ConversationMode.Human, c.Mode);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Return_to_ai_is_refused_when_the_plan_has_no_ai_assistant()
    {
        InboxSettings(planIncludesBot: false, newConversations: true, reopenedConversations: true);
        var c = Conv(ConversationMode.Human);

        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().ReturnToAiAsync(_clientId, c.Id));
        Assert.Equal(EntitlementKeys.AiAssistant, ex.Entitlement);
        Assert.Equal(ConversationMode.Human, c.Mode);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_contact_reopening_through_the_generic_path_goes_to_human_when_the_client_says_so()
    {
        InboxSettings(planIncludesBot: true, newConversations: true, reopenedConversations: false);
        var closed = Conv();
        closed.Status = ConversationStatus.Closed;

        await Sut().AddMessageAsync(_clientId, closed.Id, Msg("inbound", "contact"));

        Assert.Equal((ConversationStatus.Active, ConversationMode.Human), (closed.Status, closed.Mode));
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clientId, closed.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);

        var active = Conv();
        await Sut().AddMessageAsync(_clientId, active.Id, Msg("inbound", "contact"));
        Assert.Equal(ConversationMode.Ai, active.Mode);
    }

    [Fact]
    public async Task List_delegates_to_the_repository()
    {
        _conversations.Setup(r => r.ListInboxAsync(_clientId, 2, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ConversationListRow>() as IReadOnlyList<ConversationListRow>, 9));
        var (_, total) = await Sut().ListAsync(_clientId, 2, 3);
        Assert.Equal(9, total);
    }
}
