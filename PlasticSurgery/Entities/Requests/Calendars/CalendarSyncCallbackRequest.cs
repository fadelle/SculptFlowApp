namespace PlasticSurgery.Entities.Requests.Calendars;

/// <summary>Body n8n POSTs to /api/calendar-integrations/sync-callback once it has attempted the operation
/// CalendarSyncTriggerPayload asked for. RequestId must match the one SculptFlow sent, so a late/duplicate callback
/// for a superseded request (e.g. the appointment was rescheduled again before this callback arrived) is ignored.</summary>
public record CalendarSyncCallbackRequest(
    Guid ClinicId,
    Guid AppointmentId,
    Guid CalendarIntegrationId,
    Guid RequestId,
    bool Success,
    string? ExternalEventId,
    string? ErrorMessage
);
