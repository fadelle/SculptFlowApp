namespace Omni.App.Entities.Dtos.Dashboard;

/// <summary>Page 3 — Bookings row.</summary>
public record DashboardBookingRow(
    Guid Id,
    string? ContactFullName,
    string? ContactPhone,
    string? ServiceName,
    string Status,
    DateTimeOffset ScheduledStart,
    string? LocationType
);
