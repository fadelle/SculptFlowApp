namespace Omni.App.Entities.Requests.Inbox;

public record CreateConversationRequest(
    Guid ClientId,
    Guid LeadId,
    string Channel,
    string? ExternalThreadId
);
