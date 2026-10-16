namespace Omni.App.Entities.Dtos.Dashboard;

/// <summary>Page 3 — Appointments row.</summary>
public record DashboardAppointmentRow(
    Guid Id,
    string? LeadFullName,
    string? LeadPhone,
    string? ServiceName,
    string Status,
    DateTimeOffset ScheduledStart,
    string? LocationType
);
