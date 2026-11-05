namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>Who answers conversations on the account behind a client's channel, decided per message
/// (IInboxSettingsService.ResolveAsync): BotId answers unless BlockReason (AiBotBlockReason) says why the team does.
/// AnswersNew / AnswersReopened add the account's two switches. HandoffMessage is the assigned bot's message to the contact
/// when it hands a conversation to the team (null when it has none).</summary>
public record AccountAiDecision(Guid? ChannelIntegrationId, Guid? BotId, string? BlockReason, bool NewConversations, bool ReopenedConversations,
    string? HandoffMessage = null)
{
    public bool BotAnswers => BlockReason is null && BotId is not null;

    /// <summary>A brand-new conversation starts with the bot (else in human mode).</summary>
    public bool AnswersNew => BotAnswers && NewConversations;

    /// <summary>A conversation the contact reopens stays with the bot (else it moves to human mode).</summary>
    public bool AnswersReopened => BotAnswers && ReopenedConversations;
}
