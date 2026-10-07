namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>One event_logs row of a lead.</summary>
public record LeadEventRow(Guid Id, Guid? LeadId, Guid? ConversationId, Guid? AppointmentId, string EventType, string? Source,
    string Metadata, DateTimeOffset CreatedAt);
