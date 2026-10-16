namespace Omni.App.Entities.Requests.Inbox;

/// <summary>A clinic's Inbox settings: whether the AI bot answers new conversations and conversations a patient reopens.</summary>
public record UpdateInboxSettingsRequest(bool AiBotNewConversations, bool AiBotReopenedConversations);
