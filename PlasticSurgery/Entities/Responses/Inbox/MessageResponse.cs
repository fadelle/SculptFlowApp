namespace PlasticSurgery.Entities.Responses.Inbox;

public record MessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid LeadId,
    string Direction,
    string SenderType,
    string Origin,
    string Channel,
    string MessageType,
    string? Content,
    string? DeliveryStatus,
    bool IsAiGenerated,
    Guid? WhatsAppTemplateId,
    Guid? CampaignId,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt = null,
    DateTimeOffset? ReadAt = null,
    DateTimeOffset? FailedAt = null,
    DateTimeOffset? DeletedAt = null,
    string? FailureCode = null,
    string? FailureReason = null,
    string? MetadataJson = null
);
