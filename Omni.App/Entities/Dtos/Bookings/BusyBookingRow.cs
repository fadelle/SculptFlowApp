namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>A booking that blocks slots. ScheduledEnd may be missing; then the service's booking duration
/// (or the client default) applies.</summary>
public record BusyBookingRow(DateTimeOffset ScheduledStart, DateTimeOffset? ScheduledEnd, int? ServiceMinutes);
