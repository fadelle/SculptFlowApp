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
// n8n contract — Services/ICalendarSyncNotifier.cs sends these; CalendarIntegrationsIngestController
// receives the matching callbacks. Field names/casing match what n8n expects (System.Text.Json Web
// defaults: camelCase), same convention as AiTriggerPayload.
// ---------------------------------------------------------------------------------------------

/// <summary>POSTed to N8n:CalendarConnectWebhookUrl. Action: "connect" (n8n must run the OAuth flow, then list
/// calendars), "refresh_calendars" (n8n already has stored credentials for ExternalConnectionRef, just re-lists),
/// or "disconnect" (best-effort — tells n8n it may revoke/forget the stored credentials).</summary>
public record CalendarConnectTriggerPayload(
    Guid ClinicId,
    Guid CalendarIntegrationId,
    string Provider,
    string Action,
    /// <summary>Set for refresh_calendars/disconnect — the handle from a previous successful connect.</summary>
    string? ExternalConnectionRef
);

/// <summary>Body n8n POSTs to /api/calendar-integrations/connect-callback. Success=false + ErrorMessage covers a
/// failed connect attempt or a refresh that discovers the connection no longer works.</summary>
public record CalendarConnectCallbackRequest(
    Guid ClinicId,
    Guid CalendarIntegrationId,
    bool Success,
    string? ExternalConnectionRef,
    string? AccountDisplayName,
    IReadOnlyList<CalendarCalendarOption>? Calendars,
    string? ErrorMessage
);

/// <summary>POSTed to N8n:CalendarSyncWebhookUrl once a SculptFlow appointment change has already succeeded and
/// saved. ExternalEventId is set (from the stored AppointmentCalendarSync row) for update/cancel so n8n acts on the
/// SAME external event instead of creating a new one; null for create. All patient-facing text here is exactly what
/// should appear on the external event — n8n must not add anything else from elsewhere.</summary>
public record CalendarSyncTriggerPayload(
    Guid ClinicId,
    Guid AppointmentId,
    Guid CalendarIntegrationId,
    string Provider,
    string ExternalConnectionRef,
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
