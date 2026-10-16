namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>A clinic's Inbox settings with what its plan allows. PlanIncludesAiBot is the plan's ai_agent entitlement; the
/// AI bot switches are the clinic's own choice and only take effect while the plan includes the bot.</summary>
public record InboxSettingsDto(bool PlanIncludesAiBot, bool AiBotNewConversations, bool AiBotReopenedConversations)
{
    /// <summary>A brand-new conversation starts in AI mode (else human mode).</summary>
    public bool AiBotAnswersNew => PlanIncludesAiBot && AiBotNewConversations;

    /// <summary>A conversation a patient reopens may stay with the AI (else it moves to human mode).</summary>
    public bool AiBotAnswersReopened => PlanIncludesAiBot && AiBotReopenedConversations;
}
