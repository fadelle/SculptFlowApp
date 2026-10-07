namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record MessageRow(Guid Id, Guid ClinicId, string ClinicName, Guid ConversationId, string Channel, string Direction,
    string SenderType, string Origin, string? Content, string? DeliveryStatus, string? FailureCode, string? FailureReason,
    DateTimeOffset CreatedAt);
