namespace Omni.App.Entities.Requests.Calendars;

/// <summary>POSTed to N8n:CalendarSyncWebhookUrl once an Omni booking change has already succeeded and
/// saved. ExternalEventId is set (from the stored BookingCalendarSync row) for update/cancel so n8n acts on the
/// SAME external event instead of creating a new one; null for create. AccessToken is a short-lived OAuth token
/// Omni refreshed just before sending this — n8n uses it as-is and must not try to refresh or store it. All
/// patient-facing text here is exactly what should appear on the external event — n8n must not add anything else
/// from elsewhere.</summary>
public record CalendarSyncTriggerPayload(
    Guid ClientId,
    Guid BookingId,
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
    /// <summary>The client's IANA timezone id — ScheduledStart/End already carry the correct offset; this is for
    /// display only. n8n must never recompute or reinterpret the time.</summary>
    string Timezone,
    string EventTitle,
    string? EventDescription,
    string DashboardLink
);
