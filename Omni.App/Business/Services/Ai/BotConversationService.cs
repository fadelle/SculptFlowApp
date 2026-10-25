using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Services.Ai;

public class BotConversationService : IBotConversationService
{
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly IBotTestService _tests;

    public BotConversationService(IConversationService conversations, IMessageService messages, IBotTestService tests)
    {
        _conversations = conversations;
        _messages = messages;
        _tests = tests;
    }

    public async Task<BotReplyOutcome?> ReplyAsync(BotRunContext run, Guid conversationId, string? content, CancellationToken ct = default)
    {
        if (!run.IsTest)
        {
            var message = await _messages.SendAiReplyAsync(run.ClientId, conversationId, content ?? string.Empty, ct);
            return message is null ? null : new BotReplyOutcome(message.Id, Test: false);
        }

        // A test run's "conversation" is its test session.
        if (!await _tests.ReplyAsync(run, content, ct)) throw new AiReplyRefusedException("test_reply_refused"); // text-ok: a reason code the workflow reads
        return new BotReplyOutcome(null, Test: true);
    }

    public async Task<bool?> HandOffAsync(BotRunContext run, Guid conversationId, string? reason, CancellationToken ct = default)
    {
        if (run.IsTest) return await _tests.HandoffAsync(run, reason, ct);

        var result = await _conversations.HandoffToHumanAsync(run.ClientId, conversationId, reason, ct);
        if (result is null) return null;
        if (!result.HandedOff) return false;

        // Only a real ai -> human change sends it: the row lock in HandoffToHumanAsync makes retries and repeats see "human".
        if (!string.IsNullOrWhiteSpace(run.HandoffMessage))
        {
            await _messages.SendHandoffMessageAsync(run.ClientId, conversationId, run.BotId, run.HandoffMessage, ct);
        }
        return true;
    }
}
