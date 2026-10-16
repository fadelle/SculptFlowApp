namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record EventRow(Guid Id, Guid ClientId, string ClientName, string EventType, string? Source, Guid? LeadId,
    Guid? ConversationId, Guid? AppointmentId, string Metadata, DateTimeOffset CreatedAt);
