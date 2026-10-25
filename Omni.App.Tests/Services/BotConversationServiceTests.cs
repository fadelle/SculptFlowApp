using Omni.App.Business.Services.Ai;

namespace Omni.App.Tests.Services;

public class BotConversationServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _conversation = Guid.NewGuid();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IMessageService> _messages = new();
    private readonly Mock<IBotTestService> _tests = new();

    private BotConversationService Sut() => new(_conversations.Object, _messages.Object, _tests.Object);

    private BotRunContext Run(string? handoffMessage = "Moved to the team.", Guid? testSession = null) =>
        new(_clientId, Guid.NewGuid(), new HashSet<string>(), null, handoffMessage, testSession);

    private void Handoff(bool handedOff) =>
        _conversations.Setup(c => c.HandoffToHumanAsync(_clientId, _conversation, "asked", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HandoffResult(new ConversationResponse(_conversation, _clientId, Guid.NewGuid(), "whatsapp", "active", "human", false, true, null, null, null, false, DateTimeOffset.UtcNow), handedOff));

    [Fact]
    public async Task A_real_handoff_sends_the_bots_message_once_and_a_repeat_sends_nothing()
    {
        var run = Run();
        Handoff(handedOff: true);
        Assert.True(await Sut().HandOffAsync(run, _conversation, "asked"));
        _messages.Verify(m => m.SendHandoffMessageAsync(_clientId, _conversation, run.BotId, "Moved to the team.", It.IsAny<CancellationToken>()), Times.Once);

        Handoff(handedOff: false);
        Assert.False(await Sut().HandOffAsync(run, _conversation, "asked"));
        _messages.Verify(m => m.SendHandoffMessageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_handoff_message_is_set_or_the_conversation_is_unknown()
    {
        Handoff(handedOff: true);
        Assert.True(await Sut().HandOffAsync(Run(handoffMessage: " "), _conversation, "asked"));
        Assert.Null(await Sut().HandOffAsync(Run(), Guid.NewGuid(), "asked"));
        _messages.Verify(m => m.SendHandoffMessageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_live_reply_is_sent_as_the_ai_and_a_test_reply_joins_the_transcript()
    {
        var message = new MessageResponse(Guid.NewGuid(), _clientId, _conversation, "outbound", "ai", "ai", "whatsapp", "text", "hi", null, true, null, null, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null);
        _messages.Setup(m => m.SendAiReplyAsync(_clientId, _conversation, "hi", It.IsAny<CancellationToken>())).ReturnsAsync(message);
        Assert.Equal(new BotReplyOutcome(message.Id, false), await Sut().ReplyAsync(Run(), _conversation, "hi"));
        Assert.Null(await Sut().ReplyAsync(Run(), Guid.NewGuid(), "hi"));

        var test = Run(testSession: Guid.NewGuid());
        _tests.Setup(t => t.ReplyAsync(test, "hi", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.Equal(new BotReplyOutcome(null, true), await Sut().ReplyAsync(test, test.TestSessionId!.Value, "hi"));
        _tests.Setup(t => t.ReplyAsync(test, "hi", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<AiReplyRefusedException>(() => Sut().ReplyAsync(test, test.TestSessionId!.Value, "hi"));

        _tests.Setup(t => t.HandoffAsync(test, "asked", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.True(await Sut().HandOffAsync(test, test.TestSessionId!.Value, "asked"));
        _conversations.Verify(c => c.HandoffToHumanAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
