namespace Omni.App.Entities.Requests.Inbox;

/// <summary>Body for POST /api/conversations/{id}/messages/send: a member's reply from the Inbox (flips the conversation to
/// human mode). See ConversationsController.SendMessage / MessageService.SendAsync.</summary>
public record SendMessageRequest(string Content, string? MessageType = null);
