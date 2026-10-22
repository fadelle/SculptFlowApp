namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>A client's Inbox settings with what its plan allows. PlanIncludesAiBot is the plan's ai_assistant entitlement; the
/// AI bot switches are the client's own choice and only take effect while the plan includes the bot. ChannelsWithoutAiBot are
/// connected channels beyond the plan's max_ai_bots (plus add-ons): an AI bot is a connected channel the AI answers on,
/// counted in connection order, so the bot doesn't answer on the later ones.</summary>
public record InboxSettingsDto(bool PlanIncludesAiBot, bool AiBotNewConversations, bool AiBotReopenedConversations,
    IReadOnlySet<string>? ChannelsWithoutAiBot = null, int? AiBotLimit = null)
{
    /// <summary>A brand-new conversation starts in AI mode (else human mode).</summary>
    public bool AiBotAnswersNew => PlanIncludesAiBot && AiBotNewConversations;

    /// <summary>A conversation a contact reopens may stay with the AI (else it moves to human mode).</summary>
    public bool AiBotAnswersReopened => PlanIncludesAiBot && AiBotReopenedConversations;

    /// <summary>The AI bot may answer on this channel (the plan includes it and the channel is within the AI bot limit).</summary>
    public bool AiBotAnswersOn(string channel) => PlanIncludesAiBot && ChannelsWithoutAiBot?.Contains(channel) != true;
}
