using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Business.Contracts.Services.Inbox;

/// <summary>
/// AI per connected account (crm.inbox_settings): where and when a bot answers. Rules: one bot per account (a bot may serve
/// several); an account answers with its bot only while the plan includes the AI assistant, AI is on there, the bot is
/// active and within the plan's max_ai_bots; otherwise the team answers (human mode: same state as a handoff, so n8n is never
/// called and nothing AI is charged). Turning AI off on one account never touches another.
/// </summary>
public interface IInboxSettingsService
{
    /// <summary>The Inbox Settings page: every connected account with its settings (defaults, AI off, when it has no row).
    /// Throws KeyNotFoundException when the client doesn't exist.</summary>
    Task<InboxSettingsDto> GetAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Who answers conversations on the client's account for <paramref name="channel"/> (cached reads, so cheap per
    /// message).</summary>
    Task<AccountAiDecision> ResolveAsync(Guid clientId, string channel, CancellationToken ct = default);

    /// <summary>The mode a brand-new conversation on <paramref name="channel"/> starts in: ConversationMode.Ai when the account's
    /// bot answers new conversations, else ConversationMode.Human.</summary>
    Task<string> InitialModeAsync(Guid clientId, string channel, CancellationToken ct = default);

    /// <summary>Saves one account's settings, creating its row on first save. Turning AI on needs the plan's AI assistant
    /// (EntitlementDeniedException) and an active bot of the client's (ArgumentException); a disconnected or unknown account is
    /// KeyNotFoundException.</summary>
    Task UpdateAccountAsync(Guid clientId, Guid channelIntegrationId, UpdateAccountAiRequest request, CancellationToken ct = default);
}
