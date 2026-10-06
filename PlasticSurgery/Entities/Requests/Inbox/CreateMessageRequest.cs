namespace PlasticSurgery.Entities.Requests.Inbox;

public record CreateMessageRequest(
    string Direction,
    string SenderType,
    string Channel,
    string? MessageType,
    string? Content,
    string? ExternalMessageId,
    bool IsAiGenerated = false,
    string? Origin = null
);
