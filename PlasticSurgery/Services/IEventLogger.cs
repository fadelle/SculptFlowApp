using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>Writes rows to the "events" table for analytics/debugging. Does not call SaveChanges —
/// the caller's SaveChangesAsync (usually inside the same service method) persists it together with
/// the business change, so an event is never logged for a write that didn't actually commit.</summary>
public interface IEventLogger
{
    void Log(Guid clinicId, string eventType, Guid? leadId = null, Guid? conversationId = null,
        Guid? appointmentId = null, string? source = null, string metadataJson = "{}");
}

public class EventLogger : IEventLogger
{
    private readonly Data.ApplicationDbContext _db;

    public EventLogger(Data.ApplicationDbContext db)
    {
        _db = db;
    }

    public void Log(Guid clinicId, string eventType, Guid? leadId = null, Guid? conversationId = null,
        Guid? appointmentId = null, string? source = null, string metadataJson = "{}")
    {
        _db.Events.Add(new EventLog
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            LeadId = leadId,
            ConversationId = conversationId,
            AppointmentId = appointmentId,
            EventType = eventType,
            Source = source,
            Metadata = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
