using PlasticSurgery.Business.Services.Inbox;

namespace PlasticSurgery.Tests.Services;

public class ConversationServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _leadId = Guid.NewGuid();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<ILeadRepository> _leads = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IInboxSettingsService> _inboxSettings = new();

    public ConversationServiceTests() => InboxSettings(planIncludesBot: true, newConversations: true, reopenedConversations: true);

    private ConversationService Sut() => new(_conversations.Object, _messages.Object, _leads.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object, _inboxSettings.Object);

    private void InboxSettings(bool planIncludesBot, bool newConversations, bool reopenedConversations)
    {
        var settings = new InboxSettingsDto(planIncludesBot, newConversations, reopenedConversations);
        _inboxSettings.Setup(a => a.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _inboxSettings.Setup(a => a.InitialModeAsync(_clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings.AiBotAnswersNew ? ConversationMode.Ai : ConversationMode.Human);
    }

    private Conversation Conv(string mode = ConversationMode.Ai, string? externalThread = null)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(), ClinicId = _clinicId, LeadId = _leadId, Channel = ConversationChannel.WhatsApp, Mode = mode,
            Status = ConversationStatus.Active, AiEnabled = mode == ConversationMode.Ai, HumanTakeover = mode == ConversationMode.Human, ExternalThreadId = externalThread
        };
        _conversations.Setup(r => r.GetAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    [Fact]
    public async Task Create_validates_channel_and_starts_in_ai_mode()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateConversationRequest(_clinicId, _leadId, "pigeon", null)));
        _leads.Setup(l => l.GetAsync(_clinicId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(new Lead { Id = _leadId, ClinicId = _clinicId });
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);
        var r = await Sut().CreateAsync(new CreateConversationRequest(_clinicId, _leadId, ConversationChannel.Telegram, "t1"));
        Assert.Equal((ConversationMode.Ai, true, false, ConversationStatus.Active, "t1"), (added!.Mode, added.AiEnabled, added.HumanTakeover, added.Status, added.ExternalThreadId));
        Assert.Equal(added.Id, r.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_for_a_lead_this_clinic_doesnt_have_is_not_found()
    {
        // Covers both a lead that doesn't exist and another clinic's lead: the lookup is scoped to the clinic.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CreateAsync(new CreateConversationRequest(_clinicId, Guid.NewGuid(), ConversationChannel.Website, null)));
        _conversations.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateForLead_reuses_an_existing_conversation()
    {
        var c = Conv();
        _conversations.Setup(r => r.FindForLeadAsync(_clinicId, _leadId, "whatsapp", It.IsAny<CancellationToken>())).ReturnsAsync(c);
        Assert.Equal(c.Id, (await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "whatsapp")).Id);
        _conversations.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateForLead_creates_when_missing()
    {
        await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "whatsapp");
        _conversations.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateForLead_with_thread_fills_a_missing_thread_id_only()
    {
        var blank = Conv();
        _conversations.Setup(r => r.FindForLeadAsync(_clinicId, _leadId, "telegram", It.IsAny<CancellationToken>())).ReturnsAsync(blank);
        await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "telegram", "chat-1");
        Assert.Equal("chat-1", blank.ExternalThreadId);

        await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "telegram", "chat-2");
        Assert.Equal("chat-1", blank.ExternalThreadId);
    }

    [Fact]
    public async Task GetOrCreateForLead_with_thread_creates_validates_and_yields_to_a_concurrent_winner()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "pigeon", "x"));

        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);
        Assert.Equal("th", (await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "telegram", "th")) is { } r && added!.ExternalThreadId == "th" ? "th" : "");

        var winner = Conv(externalThread: "th2");
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _conversations.Setup(x => x.FindByExternalThreadAsync(_clinicId, "telegram", "th2", It.IsAny<CancellationToken>())).ReturnsAsync(winner);
        Assert.Equal(winner.Id, (await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "telegram", "th2")).Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "telegram", "th3"));
    }

    [Fact]
    public async Task GetByIdWithMessages_returns_null_or_both_parts()
    {
        Assert.Null(await Sut().GetByIdWithMessagesAsync(_clinicId, Guid.NewGuid()));
        var c = Conv();
        _messages.Setup(m => m.ListForConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message> { new() { Id = Guid.NewGuid(), ConversationId = c.Id, Content = "hi" } });
        var result = await Sut().GetByIdWithMessagesAsync(_clinicId, c.Id);
        Assert.Equal(c.Id, result!.Value.Conversation.Id);
        Assert.Equal("hi", Assert.Single(result.Value.Messages).Content);
    }

    [Fact]
    public async Task GetMessages_is_empty_for_a_foreign_conversation()
    {
        Assert.Empty(await Sut().GetMessagesAsync(_clinicId, Guid.NewGuid()));
        var c = Conv();
        _conversations.Setup(r => r.ExistsAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _messages.Setup(m => m.ListForConversationAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message> { new() { Id = Guid.NewGuid() } });
        Assert.Single(await Sut().GetMessagesAsync(_clinicId, c.Id));
        Assert.True(await Sut().BelongsToClinicAsync(_clinicId, c.Id));
    }

    // ---------------------------------------------------------------- AddMessage

    private static CreateMessageRequest Msg(string direction, string sender, string channel = "whatsapp", string? type = null, string? origin = null) =>
        new(direction, sender, channel, type, "hello", "ext", false, origin);

    [Fact]
    public async Task AddMessage_validates_direction_and_returns_null_for_unknown_conversation()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddMessageAsync(_clinicId, Guid.NewGuid(), Msg("sideways", "lead")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddMessageAsync(_clinicId, Guid.NewGuid(), Msg("inbound", "martian")));
        Assert.Null(await Sut().AddMessageAsync(_clinicId, Guid.NewGuid(), Msg("inbound", "lead")));
    }

    [Fact]
    public async Task Outbound_message_stamps_conversation_and_moves_a_new_lead_to_contacted()
    {
        var c = Conv();
        var lead = new Lead { Id = _leadId, Status = LeadStatus.New };
        _leads.Setup(l => l.GetByIdAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);

        var r = await Sut().AddMessageAsync(_clinicId, c.Id, Msg("outbound", "staff"));

        Assert.Equal(("text", MessageOrigin.Dashboard, "hello"), (saved!.MessageType, saved.Origin, saved.Content));
        Assert.NotNull(saved.SentAt);
        Assert.Null(saved.ReceivedAt);
        Assert.Equal(("outbound", saved.CreatedAt), (c.LastMessageDirection, c.LastMessageAt!.Value));
        Assert.Equal(LeadStatus.Contacted, lead.Status);
        Assert.NotNull(lead.LastContactAt);
        Assert.Equal(saved.Id, r!.Id);
    }

    [Fact]
    public async Task Inbound_message_keeps_the_lead_status_and_sets_received_time()
    {
        var c = Conv();
        var lead = new Lead { Id = _leadId, Status = LeadStatus.New };
        _leads.Setup(l => l.GetByIdAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().AddMessageAsync(_clinicId, c.Id, Msg("inbound", "lead"));
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.NotNull(saved!.ReceivedAt);
        Assert.Null(saved.SentAt);
        Assert.Equal(MessageOrigin.WhatsAppCustomer, saved.Origin);
    }

    [Theory]
    [InlineData("ai", "whatsapp", MessageOrigin.Ai)]
    [InlineData("staff", "whatsapp", MessageOrigin.Dashboard)]
    [InlineData("lead", "whatsapp", MessageOrigin.WhatsAppCustomer)]
    [InlineData("lead", "telegram", MessageOrigin.TelegramCustomer)]
    [InlineData("lead", "instagram", MessageOrigin.System)]
    [InlineData("system", "whatsapp", MessageOrigin.System)]
    public async Task Origin_is_inferred_from_sender_and_channel(string sender, string channel, string expected)
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().AddMessageAsync(_clinicId, c.Id, Msg("inbound", sender, channel));
        Assert.Equal(expected, saved!.Origin);
    }

    [Fact]
    public async Task An_explicit_origin_wins_over_inference()
    {
        var c = Conv();
        Message? saved = null;
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(m => saved = m);
        await Sut().AddMessageAsync(_clinicId, c.Id, Msg("outbound", "staff", type: "image", origin: MessageOrigin.Campaign));
        Assert.Equal((MessageOrigin.Campaign, "image"), (saved!.Origin, saved.MessageType));
    }

    // ---------------------------------------------------------------- mode / read / close

    [Fact]
    public async Task MarkRead_notifies_only_when_a_row_changed()
    {
        var id = Guid.NewGuid();
        Assert.False(await Sut().MarkReadAsync(_clinicId, id));
        _notifier.Verify(n => n.ConversationUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _conversations.Setup(r => r.MarkReadAsync(_clinicId, id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Assert.True(await Sut().MarkReadAsync(_clinicId, id));
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clinicId, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TakeOver_and_ReturnToAi_switch_mode_log_and_broadcast()
    {
        var c = Conv();
        var human = await Sut().TakeOverAsync(_clinicId, c.Id);
        Assert.Equal((ConversationMode.Human, true), (human!.Mode, human.HumanTakeover));
        _events.Verify(e => e.Log(_clinicId, EventTypes.HumanHandoff, null, c.Id, null, "dashboard", "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clinicId, c.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);

        var ai = await Sut().ReturnToAiAsync(_clinicId, c.Id);
        Assert.Equal((ConversationMode.Ai, true), (ai!.Mode, ai.AiEnabled));
        Assert.Contains(c.Messages, m => m.Content == "Returned to AI");
        Assert.Null(await Sut().TakeOverAsync(_clinicId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Handoff_notifies_staff_once_and_not_when_already_human()
    {
        var c = Conv();
        _leads.Setup(l => l.GetFullNameAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync("Ann Lee");
        await Sut().HandoffToHumanAsync(_clinicId, c.Id, "wants a price");
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.Handoff, "AI handed off to staff", "Ann Lee needs staff attention: wants a price",
            _leadId, c.Id, null, null, $"/inbox?conversationId={c.Id}", It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clinicId, EventTypes.HumanHandoff, null, c.Id, null, MessageOrigin.Ai, "{\"reason\":\"wants a price\"}"), Times.Once);

        _conversations.Setup(r => r.IsInHumanModeAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().HandoffToHumanAsync(_clinicId, c.Id, null);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handoff_message_falls_back_when_name_or_reason_is_missing_and_unknown_conversation_is_null()
    {
        var c = Conv();
        await Sut().HandoffToHumanAsync(_clinicId, c.Id, " ");
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.Handoff, It.IsAny<string>(), "A conversation needs staff attention.", It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(await Sut().HandoffToHumanAsync(_clinicId, Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task Close_sets_status_logs_and_broadcasts()
    {
        var c = Conv();
        var r = await Sut().CloseAsync(_clinicId, c.Id);
        Assert.Equal(ConversationStatus.Closed, r!.Status);
        _events.Verify(e => e.Log(_clinicId, EventTypes.ConversationClosed, null, c.Id, null, "dashboard", "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clinicId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(await Sut().CloseAsync(_clinicId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Reopen_brings_a_closed_conversation_back_logs_and_broadcasts()
    {
        var c = Conv();
        c.Status = ConversationStatus.Closed;

        var r = await Sut().ReopenAsync(_clinicId, c.Id);

        Assert.Equal(ConversationStatus.Active, r!.Status);
        Assert.Equal(ConversationStatus.Active, c.Status);
        _events.Verify(e => e.Log(_clinicId, EventTypes.ConversationReopened, null, c.Id, null, "dashboard", "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clinicId, c.Id, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reopen_leaves_an_active_conversation_alone_and_returns_null_for_an_unknown_one()
    {
        var c = Conv();

        var r = await Sut().ReopenAsync(_clinicId, c.Id);

        Assert.Equal(ConversationStatus.Active, r!.Status);
        _events.Verify(e => e.Log(It.IsAny<Guid>(), EventTypes.ConversationReopened, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        _notifier.Verify(n => n.ConversationUpdatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(await Sut().ReopenAsync(_clinicId, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_patient_message_added_through_the_generic_path_reopens_but_a_staff_message_does_not()
    {
        var closedForPatient = Conv();
        closedForPatient.Status = ConversationStatus.Closed;
        await Sut().AddMessageAsync(_clinicId, closedForPatient.Id, Msg("inbound", "lead"));
        Assert.Equal(ConversationStatus.Active, closedForPatient.Status);
        _events.Verify(e => e.Log(_clinicId, EventTypes.ConversationReopened, null, closedForPatient.Id, null, MessageOrigin.WhatsAppCustomer, "{}"), Times.Once);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clinicId, closedForPatient.Id, It.IsAny<CancellationToken>()), Times.Once);

        var closedForStaff = Conv();
        closedForStaff.Status = ConversationStatus.Closed;
        await Sut().AddMessageAsync(_clinicId, closedForStaff.Id, Msg("outbound", "staff"));
        Assert.Equal(ConversationStatus.Closed, closedForStaff.Status);
        _notifier.Verify(n => n.ConversationUpdatedAsync(_clinicId, closedForStaff.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- AI bot routing

    [Theory]
    [InlineData(true, true, ConversationMode.Ai)]
    [InlineData(true, false, ConversationMode.Human)]
    [InlineData(false, true, ConversationMode.Human)]
    public async Task A_new_conversation_starts_with_the_bot_only_when_plan_and_clinic_allow_it(bool plan, bool newOn, string expected)
    {
        InboxSettings(plan, newOn, reopenedConversations: true);
        Conversation? added = null;
        _conversations.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => added = c);

        await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "whatsapp");
        Assert.Equal((expected, expected == ConversationMode.Ai, expected == ConversationMode.Human), (added!.Mode, added.AiEnabled, added.HumanTakeover));

        added = null;
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().GetOrCreateForLeadAsync(_clinicId, _leadId, "telegram", "chat-9");
        Assert.Equal(expected, added!.Mode);
    }

    [Fact]
    public async Task Return_to_ai_is_refused_when_the_plan_has_no_ai_agent()
    {
        InboxSettings(planIncludesBot: false, newConversations: true, reopenedConversations: true);
        var c = Conv(ConversationMode.Human);

        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().ReturnToAiAsync(_clinicId, c.Id));
        Assert.Equal(EntitlementKeys.AiAgent, ex.Entitlement);
        Assert.Equal(ConversationMode.Human, c.Mode);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_patient_reopening_through_the_generic_path_goes_to_staff_when_the_clinic_says_so()
    {
        InboxSettings(planIncludesBot: true, newConversations: true, reopenedConversations: false);
        var closed = Conv();
        closed.Status = ConversationStatus.Closed;

        await Sut().AddMessageAsync(_clinicId, closed.Id, Msg("inbound", "lead"));

        Assert.Equal((ConversationStatus.Active, ConversationMode.Human), (closed.Status, closed.Mode));
        _notifier.Verify(n => n.ConversationModeChangedAsync(_clinicId, closed.Id, ConversationMode.Human, It.IsAny<CancellationToken>()), Times.Once);

        var active = Conv();
        await Sut().AddMessageAsync(_clinicId, active.Id, Msg("inbound", "lead"));
        Assert.Equal(ConversationMode.Ai, active.Mode);
    }

    [Fact]
    public async Task List_delegates_to_the_repository()
    {
        _conversations.Setup(r => r.ListInboxAsync(_clinicId, 2, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ConversationListRow>() as IReadOnlyList<ConversationListRow>, 9));
        var (_, total) = await Sut().ListAsync(_clinicId, 2, 3);
        Assert.Equal(9, total);
    }
}
