namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>A client's booking reminder settings (the defaults, off, when it has none). Templates: language code →
/// WhatsApp template id.</summary>
public record ReminderSettingsDto(bool Enabled, int FirstHoursBefore, int SecondHoursBefore, IReadOnlyDictionary<string, Guid> Templates);
