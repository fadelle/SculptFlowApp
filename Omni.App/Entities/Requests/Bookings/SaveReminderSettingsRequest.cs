namespace Omni.App.Entities.Requests.Bookings;

/// <summary>The reminder settings form: on/off, hours before for each reminder (0 = off, at most 168), and the WhatsApp
/// template per language code (null or missing = none for that language).</summary>
public record SaveReminderSettingsRequest(bool Enabled, int FirstHoursBefore, int SecondHoursBefore, IReadOnlyDictionary<string, Guid?> Templates);
