using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Requests.Bookings;

namespace Omni.App.Business.Contracts.Services.Bookings;

/// <summary>
/// Booking reminders: each client may turn on two reminders (default 24 and 2 hours before a booked or confirmed
/// booking). BookingReminderWorker calls <see cref="SendDueAsync"/> for every client with reminders on. A reminder goes to
/// the contact's latest WhatsApp or Telegram conversation (a new WhatsApp one when there is none but a phone number):
/// WhatsApp through the approved template chosen for the contact's language (stored language, else the client default,
/// else any chosen template), Telegram as text from the strings file. Each (booking, reminder, start time) gets one
/// scheduling.booking_reminders row, written before sending; skipped and failed ones say why (ReminderReason). Nothing is
/// sent to an opted-out contact, while the plan lacks bookings, or when Reminders:Enabled is off.
/// </summary>
public interface IBookingReminderService
{
    /// <summary>The client's settings, or the defaults (off, 24 h and 2 h, no templates).</summary>
    Task<ReminderSettingsDto> GetSettingsAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Hours 0–168 (ArgumentException reminders.errors.hours otherwise); templates must be the client's own
    /// (ArgumentException reminders.errors.template) for a language the dashboard offers.</summary>
    Task SaveSettingsAsync(Guid clientId, SaveReminderSettingsRequest request, CancellationToken ct = default);

    /// <summary>The client's approved WhatsApp templates, by name (the settings page's choices).</summary>
    Task<IReadOnlyList<ReminderTemplateOption>> ListTemplateOptionsAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The booking's reminder rows, newest first.</summary>
    Task<IReadOnlyList<BookingReminderRow>> ListForBookingAsync(Guid clientId, Guid bookingId, CancellationToken ct = default);

    /// <summary>Clients with reminders on (empty when Reminders:Enabled is off).</summary>
    Task<IReadOnlyList<Guid>> ListClientsWithRemindersAsync(CancellationToken ct = default);

    /// <summary>Handles the client's due reminders (at most Reminders:BatchSize per reminder kind). Returns how many rows
    /// it wrote (sent, skipped or failed).</summary>
    Task<int> SendDueAsync(Guid clientId, CancellationToken ct = default);
}
