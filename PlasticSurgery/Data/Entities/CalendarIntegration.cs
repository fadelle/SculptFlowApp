namespace PlasticSurgery.Data.Entities;

/// <summary>One clinic's connection to an external calendar provider (Google or Outlook). SculptFlow never talks to the
/// provider directly and never stores its OAuth tokens — n8n owns that (see Services/ICalendarSyncNotifier.cs);
/// ExternalConnectionRef is the opaque handle n8n gave back to look its own stored credentials up by.</summary>
public class CalendarIntegration
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = CalendarIntegrationStatus.Disconnected;

    public string? ExternalConnectionRef { get; set; }
    public string? AccountDisplayName { get; set; }

    public string? SelectedCalendarId { get; set; }
    public string? SelectedCalendarName { get; set; }

    public bool SyncEnabled { get; set; }

    public bool IsHealthy { get; set; } = true;
    public string? LastProblemMessage { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<CalendarIntegrationCalendar> Calendars { get; set; } = new List<CalendarIntegrationCalendar>();
}

public static class CalendarProvider
{
    public const string Google = "google";
    public const string Outlook = "outlook";
    public static readonly IReadOnlyList<string> All = new[] { Google, Outlook };
}

public static class CalendarIntegrationStatus
{
    public const string Disconnected = "disconnected";
    /// <summary>Waiting on n8n's connect callback.</summary>
    public const string Pending = "pending";
    public const string Connected = "connected";
    public const string Error = "error";
}

/// <summary>One calendar n8n reported as available on a connected account — cached so the picker doesn't need a live
/// round trip on every page load. Replaced wholesale on connect / "refresh calendars".</summary>
public class CalendarIntegrationCalendar
{
    public Guid Id { get; set; }
    public Guid CalendarIntegrationId { get; set; }
    public string ExternalCalendarId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One (appointment, connected calendar) pair — reused across create/reschedule/cancel so the same external
/// event is updated instead of duplicated. See AppointmentService's calendar-sync hook and CalendarSyncService.</summary>
public class AppointmentCalendarSync
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid CalendarIntegrationId { get; set; }
    public string? ExternalEventId { get; set; }
    public string Status { get; set; } = AppointmentCalendarSyncStatus.Pending;
    public Guid? LastRequestId { get; set; }
    /// <summary>create | update | cancel — which operation LastRequestId is for, so a success/failure callback
    /// knows whether it should land as Synced or Canceled.</summary>
    public string? LastOperation { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class AppointmentCalendarSyncStatus
{
    public const string Pending = "pending";
    public const string Synced = "synced";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}
