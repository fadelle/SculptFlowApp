namespace PlasticSurgery.Dtos;

public record CalendarCalendarOption(string ExternalCalendarId, string Name, bool IsPrimary);

/// <summary>One provider's card on the Calendar Integrations page. Always returned even if the clinic never connected
/// this provider (Id = Guid.Empty, Status = disconnected) — same convention as ChannelIntegrationResponse, so the
/// page never has to special-case "no row yet".</summary>
public record CalendarIntegrationResponse(
    Guid Id,
    Guid ClinicId,
    string Provider,
    string Status,
    string? AccountDisplayName,
    string? SelectedCalendarId,
    string? SelectedCalendarName,
    bool SyncEnabled,
    bool IsHealthy,
    string? LastProblemMessage,
    DateTimeOffset? LastSyncedAt,
    IReadOnlyList<CalendarCalendarOption> AvailableCalendars
);

public record SelectCalendarRequest(string ExternalCalendarId);

public record SetCalendarSyncEnabledRequest(bool Enabled);

// ---------------------------------------------------------------------------------------------
// n8n contract — Services/ICalendarSyncNotifier.cs sends this; CalendarIntegrationsIngestController receives the
// matching callback. Field names/casing match what n8n expects (System.Text.Json Web defaults: camelCase), same
// convention as AiTriggerPayload. Connect/disconnect/list-calendars no longer go through n8n at all — SculptFlow
// owns that OAuth relationship directly (see ICalendarProviderClient, CalendarOAuthController). n8n's only remaining
// job is executing the actual create/update/cancel call against the provider's calendar API, using a SculptFlow-
// issued access token that is already fresh by the time this is sent.
// ---------------------------------------------------------------------------------------------

/// <summary>POSTed to N8n:CalendarSyncWebhookUrl once a SculptFlow appointment change has already succeeded and
/// saved. ExternalEventId is set (from the stored AppointmentCalendarSync row) for update/cancel so n8n acts on the
/// SAME external event instead of creating a new one; null for create. AccessToken is a short-lived OAuth token
/// SculptFlow refreshed just before sending this — n8n uses it as-is and must not try to refresh or store it. All
/// patient-facing text here is exactly what should appear on the external event — n8n must not add anything else
/// from elsewhere.</summary>
public record CalendarSyncTriggerPayload(
    Guid ClinicId,
    Guid AppointmentId,
    Guid CalendarIntegrationId,
    string Provider,
    string AccessToken,
    string ExternalCalendarId,
    /// <summary>create | update | cancel</summary>
    string Operation,
    Guid RequestId,
    string? ExternalEventId,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    /// <summary>The clinic's IANA timezone id — ScheduledStart/End already carry the correct offset; this is for
    /// display only. n8n must never recompute or reinterpret the time.</summary>
    string Timezone,
    string EventTitle,
    string? EventDescription,
    string SculptFlowAppointmentLink
);

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
