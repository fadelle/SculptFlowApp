namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>One connected account (channel connection) with its AI settings: the client's choices (AiEnabled, BotId and the
/// two switches; the defaults when it has no row yet), the assigned bot's name and status, and the outcome: BlockReason
/// (AiBotBlockReason) says why no bot answers there, null when one does.</summary>
public record AccountAiSettingsDto(
    Guid ChannelIntegrationId,
    string Channel,
    string? DisplayName,
    bool AiEnabled,
    Guid? BotId,
    bool AiBotNewConversations,
    bool AiBotReopenedConversations,
    string? BotName,
    string? BotStatus,
    string? BlockReason)
{
    public bool BotAnswers => BlockReason is null;
}
