namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record EventRow(Guid Id, Guid ClinicId, string ClinicName, string EventType, string? Source, Guid? LeadId,
    Guid? ConversationId, Guid? AppointmentId, string Metadata, DateTimeOffset CreatedAt);
