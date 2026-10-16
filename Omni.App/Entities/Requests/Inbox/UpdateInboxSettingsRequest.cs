namespace Omni.App.Entities.Requests.Inbox;

/// <summary>A client's Inbox settings: whether the AI bot answers new conversations and conversations a contact reopens.</summary>
public record UpdateInboxSettingsRequest(bool AiBotNewConversations, bool AiBotReopenedConversations);
