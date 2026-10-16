namespace Omni.App.Entities.Dtos.Appointments;

/// <summary>An appointment that blocks slots. ScheduledEnd may be missing; then the procedure's consultation duration
/// (or the client default) applies.</summary>
public record BusyAppointmentRow(DateTimeOffset ScheduledStart, DateTimeOffset? ScheduledEnd, int? ProcedureMinutes);
