using System.Text.Json;
using Omni.App.Business.Services.Ai;

namespace Omni.App.Tests.Services;

public class BotTestServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IBotRepository> _bots = new();
    private readonly Mock<IBotTestRepository> _tests = new();
    private readonly Mock<IBotRuntimeService> _runtime = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IAiTriggerNotifier> _trigger = new();
    private readonly Mock<IMessageBillingService> _billing = new();
    private readonly Mock<IAvailabilityService> _availability = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<BotTestSession> _sessions = [];
    private readonly List<BotTestMessage> _lines = [];
    private readonly Bot _bot;
    private readonly DateTimeOffset _slot = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(3).AddHours(14), TimeSpan.Zero);

    public BotTestServiceTests()
    {
        _bot = new Bot { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Front desk", Template = BotTemplate.Booking, Status = BotStatus.Draft };
        _bots.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => [_bot]);
        _tests.Setup(r => r.AddSession(It.IsAny<BotTestSession>())).Callback<BotTestSession>(_sessions.Add);
        _tests.Setup(r => r.AddMessage(It.IsAny<BotTestMessage>())).Callback<BotTestMessage>(_lines.Add);
        _tests.Setup(r => r.GetSessionAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _sessions.FirstOrDefault(s => s.Id == id));
        _tests.Setup(r => r.GetSessionForUpdateAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _sessions.FirstOrDefault(s => s.Id == id));
        _runtime.Setup(r => r.ConfigAsync(_clientId, _bot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(BotKit.Config with { Id = _bot.Id });
        _trigger.Setup(t => t.NotifyTestAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clients.Setup(r => r.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Timezone = "UTC" });
        _config.SetupGet(c => c.BotsTestMessageMaxLength).Returns(20);
        _config.SetupGet(c => c.BotsTestSessionHours).Returns(24);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IUnitOfWorkTransaction>());
        var date = DateOnly.FromDateTime(_slot.UtcDateTime);
        _availability.Setup(a => a.GetSlotsAsync(_clientId, It.IsAny<Guid?>(), date, 1, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AvailabilityResponse(true, "UTC", "2026-01-01", 30, "x", "y", null,
                [new AvailableSlotResponse(_slot, _slot.AddMinutes(30), date.ToString("yyyy-MM-dd"), "14:00", "label")]));
    }

    private BotTestService Sut() => new(_bots.Object, _tests.Object, _runtime.Object, _entitlements.Object, _trigger.Object, _billing.Object,
        _availability.Object, _clients.Object, _config.Object, _uow.Object);

    private BotRunContext Run(BotTestSession session, string? handoffMessage = "Moved to the team.") =>
        new(_clientId, _bot.Id, BotCapability.All.ToHashSet(), null, handoffMessage, session.Id);

    private BotTestSession Session()
    {
        var session = new BotTestSession { Id = Guid.NewGuid(), ClientId = _clientId, BotId = _bot.Id };
        _sessions.Add(session);
        return session;
    }

    private IEnumerable<string> NoteKeys() => _lines.Where(l => l.Role == BotTestRole.Note).Select(l => JsonSerializer.Deserialize<BotTestNote>(l.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Key);

    [Fact]
    public async Task A_first_message_starts_a_session_and_runs_the_real_workflow_for_the_bot_as_a_test()
    {
        var sessionId = await Sut().SendAsync(_clientId, _bot.Id, null, "  Hi there ");

        var session = Assert.Single(_sessions);
        Assert.Equal((sessionId, _bot.Id), (session.Id, session.BotId));
        Assert.Equal((BotTestRole.Contact, "Hi there"), (_lines[0].Role, _lines[0].Content));
        _tests.Verify(t => t.DeleteSessionsOlderThanAsync(It.Is<DateTimeOffset>(d => d < DateTimeOffset.UtcNow.AddHours(-23)), It.IsAny<CancellationToken>()), Times.Once);
        _trigger.Verify(t => t.NotifyTestAsync(It.Is<AiTriggerPayload>(p => p.TestSessionId == sessionId && p.ConversationId == sessionId
            && p.ContactId == sessionId && p.Channel == BotTestService.TestChannel && p.MessageText == "Hi there" && p.Bot!.Id == _bot.Id && p.MessageId == _lines[0].Id),
            It.IsAny<CancellationToken>()), Times.Once);

        // The next message continues it.
        Assert.Equal(sessionId, await Sut().SendAsync(_clientId, _bot.Id, sessionId, "Again"));
        Assert.Single(_sessions);
        _tests.Verify(t => t.DeleteSessionsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_message_needs_text_the_plan_a_ready_bot_and_a_live_session_of_that_bot()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendAsync(_clientId, _bot.Id, null, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SendAsync(_clientId, _bot.Id, null, new string('x', 21)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SendAsync(_clientId, Guid.NewGuid(), null, "Hi"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SendAsync(_clientId, _bot.Id, Guid.NewGuid(), "Hi"));

        var handedOff = Session();
        handedOff.HandedOff = true;
        Assert.Equal("bots.test.errors.handedOff",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clientId, _bot.Id, handedOff.Id, "Hi"))).Message);

        _bot.Template = BotTemplate.Custom; // no instructions yet
        Assert.Equal("bots.errors.instructionsRequired",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clientId, _bot.Id, null, "Hi"))).Message);

        _entitlements.Setup(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.AiAssistant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.AiAssistant, EntitlementDeniedException.NotIncludedKey));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SendAsync(_clientId, _bot.Id, null, "Hi"));
        _trigger.Verify(t => t.NotifyTestAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unreachable_workflow_is_noted_in_the_transcript()
    {
        _trigger.Setup(t => t.NotifyTestAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Sut().SendAsync(_clientId, _bot.Id, null, "Hi");
        Assert.Equal([BotTestNote.NotReachableKey], NoteKeys());
    }

    [Fact]
    public async Task A_reply_is_charged_like_a_live_ai_reply_and_a_refusal_is_noted_instead()
    {
        var session = Session();
        var hold = new MessageBillingHold(_clientId, "k", BillingSettlementTrigger.OnProviderAccepted);
        _billing.Setup(b => b.ReserveAiReplyAsync(_clientId, null, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(hold);

        Assert.True(await Sut().ReplyAsync(Run(session), " Hello! "));
        var reply = Assert.Single(_lines);
        Assert.Equal((BotTestRole.Bot, "Hello!"), (reply.Role, reply.Content));
        _billing.Verify(b => b.ReserveAiReplyAsync(_clientId, null, reply.Id, It.IsAny<CancellationToken>()), Times.Once);
        _billing.Verify(b => b.SentAsync(hold, It.IsAny<CancellationToken>()), Times.Once);

        _billing.Setup(b => b.ReserveAiReplyAsync(_clientId, null, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(BillingDeniedException.For(UsageFailureReason.InsufficientFunds));
        Assert.False(await Sut().ReplyAsync(Run(session), "Again"));
        Assert.Equal([BotTestNote.ReplyRefusedKey], NoteKeys());
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ReplyAsync(Run(session), " "));
    }

    [Fact]
    public async Task A_handoff_ends_the_test_once_with_the_bots_handoff_message()
    {
        var session = Session();
        Assert.True(await Sut().HandoffAsync(Run(session), "asked for a person"));
        Assert.True(session.HandedOff);
        Assert.Equal([BotTestNote.HandedOffKey], NoteKeys());
        Assert.Equal("Moved to the team.", Assert.Single(_lines, l => l.Role == BotTestRole.Automatic).Content);

        // A retry finds it handed off already: nothing more.
        Assert.False(await Sut().HandoffAsync(Run(session), "again"));
        Assert.Equal(2, _lines.Count);

        var quiet = Session();
        Assert.True(await Sut().HandoffAsync(Run(quiet, handoffMessage: null), null));
        Assert.DoesNotContain(_lines, l => l.SessionId == quiet.Id && l.Role == BotTestRole.Automatic);
    }

    [Fact]
    public async Task Scheduling_is_simulated_against_the_real_slots_and_the_test_contacts_one_booking()
    {
        var session = Session();
        var run = Run(session);

        var booked = await Sut().ScheduleAsync(run, new ScheduleBookingRequest(_slot));
        Assert.Equal((true, "BOOKED", "created"), (booked.Success, booked.Code, booked.Operation));
        Assert.Equal(_slot, session.SimulatedBookingStart);

        // Already booked: moving needs the contact's consent, like the live tool.
        var confirm = await Sut().ScheduleAsync(run, new ScheduleBookingRequest(_slot));
        Assert.Equal(("CONFIRMATION_REQUIRED", false), (confirm.Code, confirm.Success));
        Assert.Single(confirm.ExistingUpcomingBookings!);

        var moved = await Sut().ScheduleAsync(run, new ScheduleBookingRequest(_slot, ConfirmReplaceExisting: true));
        Assert.Equal(("RESCHEDULED", true), (moved.Code, moved.Success));
        Assert.NotNull(moved.PreviousBooking);

        var taken = await Sut().ScheduleAsync(run, new ScheduleBookingRequest(_slot.AddMinutes(7), ConfirmReplaceExisting: true));
        Assert.Equal(("SLOT_UNAVAILABLE", false), (taken.Code, taken.Success));
        Assert.Equal([BotTestNote.BookedKey, BotTestNote.RescheduledKey], NoteKeys());
        Assert.Single((await Sut().GetUpcomingAsync(run)).Bookings);
        Assert.False((await Sut().GetBookingContextAsync(run)).CanCreateNewBooking);
    }

    [Fact]
    public async Task Cancelling_is_simulated_on_the_test_contacts_booking()
    {
        var session = Session();
        var run = Run(session);
        Assert.Equal("NO_UPCOMING_BOOKING", (await Sut().CancelAsync(run)).Code);

        session.SimulatedBookingStart = _slot;
        session.SimulatedBookingEnd = _slot.AddMinutes(30);
        var canceled = await Sut().CancelAsync(run);
        Assert.Equal((true, "CANCELED", "canceled"), (canceled.Success, canceled.Code, canceled.Booking!.Status));
        Assert.Null(session.SimulatedBookingStart);
        Assert.Empty((await Sut().GetUpcomingAsync(run)).Bookings);
        Assert.True((await Sut().GetBookingContextAsync(run)).CanCreateNewBooking);
    }

    [Fact]
    public async Task Contact_and_stage_updates_are_only_noted_and_the_language_isnt()
    {
        var run = Run(Session());
        await Sut().NoteContactUpdateAsync(run, new UpdateContactContextRequest(null, "ar", null, null, null, null, null));
        Assert.Empty(_lines);
        await Sut().NoteContactUpdateAsync(run, new UpdateContactContextRequest(null, null, "Beirut", null, null, null, null, "booked"));
        Assert.Equal([BotTestNote.ContactUpdatedKey, BotTestNote.StageMovedKey], NoteKeys());
    }

    [Fact]
    public async Task Reading_and_resetting_only_work_on_the_bots_own_session()
    {
        var session = Session();
        Assert.NotNull(await Sut().GetAsync(_clientId, _bot.Id, session.Id));
        Assert.Null(await Sut().GetAsync(_clientId, Guid.NewGuid(), session.Id));
        Assert.Null(await Sut().GetAsync(_clientId, _bot.Id, Guid.NewGuid()));
        await Sut().ResetAsync(_clientId, session.Id);
        _tests.Verify(t => t.DeleteSessionAsync(_clientId, session.Id, It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CancelAsync(Run(new BotTestSession { Id = Guid.NewGuid() })));
    }
}
