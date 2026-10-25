namespace Omni.App.Entities.Requests.Inbox;

/// <summary>Saves one connected account's AI settings (Inbox Settings page, PUT /api/platform-admin/clients/{id}/inbox-settings/
/// {channelIntegrationId}). AiEnabled needs an active BotId.</summary>
public record UpdateAccountAiRequest(bool AiEnabled, Guid? BotId, bool AiBotNewConversations, bool AiBotReopenedConversations);
