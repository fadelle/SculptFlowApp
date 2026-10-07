using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Events;

namespace PlasticSurgery.Business.Managers;

/// <summary>
/// Queues an event row through the request's shared unit of work, so it is written atomically with whatever
/// SaveChanges call the caller makes next (rather than in its own separate round-trip).
/// </summary>
public class EventLogger : IEventLogger
{
    private readonly IEventLogRepository _events;

    public EventLogger(IEventLogRepository events)
    {
        _events = events;
    }

    public void Log(Guid clinicId, string eventType, Guid? leadId = null, Guid? conversationId = null,
        Guid? appointmentId = null, string? source = null, string metadataJson = "{}")
    {
        _events.Add(new EventLog
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
