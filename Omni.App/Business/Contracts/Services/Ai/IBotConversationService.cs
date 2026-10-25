using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.Services.Ai;

/// <summary>
/// The bot's two conversation actions (the workflow's reply and handoff_to_human), for a live conversation or a test run
/// (IBotTestService). A handoff sends the bot's handoff message once per real ai -> human change, never for a member's own
/// take-over or a billing hand-over, and never twice for retries.
/// </summary>
public interface IBotConversationService
{
    /// <summary>Sends the bot's reply (IMessageService.SendAiReplyAsync; the conversation must still be in AI mode). Null when
    /// the conversation isn't the client's. Throws ConversationNotInAiModeException / AiReplyRefusedException like it; a test
    /// run whose reply billing refused throws AiReplyRefusedException too.</summary>
    Task<BotReplyOutcome?> ReplyAsync(BotRunContext run, Guid conversationId, string? content, CancellationToken ct = default);

    /// <summary>Hands the conversation to the team; true when this call made the change, false when it was already with the
    /// team (or the test run had already handed off), null when the conversation isn't the client's.</summary>
    Task<bool?> HandOffAsync(BotRunContext run, Guid conversationId, string? reason, CancellationToken ct = default);
}
