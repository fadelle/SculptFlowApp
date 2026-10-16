namespace Omni.App.Entities.Dtos.Dashboard;

/// <summary>Page 3 — Bookings row.</summary>
public record DashboardBookingRow(
    Guid Id,
    string? LeadFullName,
    string? LeadPhone,
    string? ServiceName,
    string Status,
    DateTimeOffset ScheduledStart,
    string? LocationType
);
