namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>One message in a contact export (both directions; media appear by type, without the file).</summary>
public record ContactExportMessage(Guid Id, Guid ConversationId, string Channel, string Direction, string SenderType, string MessageType,
    string? Content, string? DeliveryStatus, DateTimeOffset CreatedAt);
