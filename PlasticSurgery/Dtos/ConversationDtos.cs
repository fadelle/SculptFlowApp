namespace PlasticSurgery.Dtos;

public record ConversationResponse(
    Guid Id,
    Guid ClinicId,
    Guid LeadId,
    string Channel,
    string Status,
    string Mode,
    bool AiEnabled,
    bool HumanTakeover,
    DateTimeOffset? LastMessageAt,
    string? LastMessageDirection,
    DateTimeOffset? ServiceWindowExpiresAt,
    /// <summary>Backend-computed — whether a normal free-form message can be sent right now.
    /// The Inbox uses this to decide composer-vs-template-picker, but the backend enforces it
    /// independently too (see MessageService.SendAsync) rather than trusting the client.</summary>
    bool IsServiceWindowOpen,
    DateTimeOffset CreatedAt
);

public record CreateConversationRequest(
    Guid ClinicId,
    Guid LeadId,
    string Channel,
    string? ExternalThreadId
);

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
