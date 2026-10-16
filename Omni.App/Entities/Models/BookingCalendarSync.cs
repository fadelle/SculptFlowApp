using Omni.App.Common.Enums;

namespace Omni.App.Entities.Models;

/// <summary>One (booking, connected calendar) pair — reused across create/reschedule/cancel so the same external
/// event is updated instead of duplicated. See BookingService's calendar-sync hook and CalendarSyncService.</summary>
public class BookingCalendarSync
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid CalendarIntegrationId { get; set; }
    public string? ExternalEventId { get; set; }
    public string Status { get; set; } = BookingCalendarSyncStatus.Pending;
    public Guid? LastRequestId { get; set; }
    /// <summary>create | update | cancel — which operation LastRequestId is for, so a success/failure callback
    /// knows whether it should land as Synced or Canceled.</summary>
    public string? LastOperation { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
