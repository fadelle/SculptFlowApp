using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Bookings;

/// <summary>Booking reminder settings (scheduling.reminder_settings) and the reminder log (scheduling.booking_reminders).</summary>
public interface IBookingReminderRepository
{
    /// <summary>The client's settings (tracked, for a save), or null.</summary>
    Task<ReminderSettings?> GetSettingsAsync(Guid clientId, CancellationToken ct = default);

    Task<ReminderSettings?> GetSettingsReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    void AddSettings(ReminderSettings settings);

    /// <summary>Clients with reminders turned on, whose client is active.</summary>
    Task<IReadOnlyList<Guid>> ListEnabledClientIdsAsync(CancellationToken ct = default);

    /// <summary>Booked or confirmed bookings of the client starting after <paramref name="now"/> and at most
    /// <paramref name="hoursBefore"/> hours from it, with no reminder row of this kind for their current start time, earliest
    /// first.</summary>
    Task<IReadOnlyList<DueReminder>> ListDueAsync(Guid clientId, string kind, int hoursBefore, DateTimeOffset now, int limit,
        CancellationToken ct = default);

    void AddReminder(BookingReminder reminder);

    /// <summary>Writes a claimed reminder's outcome (set-based update, saved at once).</summary>
    Task CompleteReminderAsync(Guid reminderId, string status, string? reason, string? channel, Guid? messageId, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>The booking's reminders, newest first (booking page).</summary>
    Task<IReadOnlyList<BookingReminderRow>> ListForBookingAsync(Guid clientId, Guid bookingId, CancellationToken ct = default);
}
