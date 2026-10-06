using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

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
