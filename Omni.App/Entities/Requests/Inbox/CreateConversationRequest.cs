namespace Omni.App.Entities.Requests.Inbox;

public record CreateConversationRequest(
    Guid ClinicId,
    Guid LeadId,
    string Channel,
    string? ExternalThreadId
);
