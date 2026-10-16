namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record MessageRow(Guid Id, Guid ClientId, string ClientName, Guid ConversationId, string Channel, string Direction,
    string SenderType, string Origin, string? Content, string? DeliveryStatus, string? FailureCode, string? FailureReason,
    DateTimeOffset CreatedAt);
