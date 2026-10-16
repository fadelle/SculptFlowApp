namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One event_logs row of a contact.</summary>
public record ContactEventRow(Guid Id, Guid? ContactId, Guid? ConversationId, Guid? BookingId, string EventType, string? Source,
    string Metadata, DateTimeOffset CreatedAt);
