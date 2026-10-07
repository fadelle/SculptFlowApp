namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>One message of a conversation, as the admin transcript shows it.</summary>
public record MessageDetail(Guid Id, string Direction, string SenderType, string MessageType, string? Content,
    string? DeliveryStatus, DateTimeOffset? FailedAt, string? FailureCode, string? FailureReason, string Origin, Guid? CampaignId,
    DateTimeOffset CreatedAt);
