namespace Omni.App.Entities.Requests.Inbox;

public record CreateConversationRequest(
    Guid ClientId,
    Guid ContactId,
    string Channel,
    string? ExternalThreadId
);
