namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>One reminder as the booking page shows it: which one, what happened and why.</summary>
public record BookingReminderRow(string Kind, DateTimeOffset ScheduledStart, string Status, string? Reason, string? Channel, DateTimeOffset UpdatedAt);
