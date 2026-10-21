namespace Omni.App.Entities.Models;

/// <summary>A client's booking reminders (scheduling.reminder_settings; no row = off): two reminders, each so many hours
/// before a booking (0 = that one is off), and the WhatsApp template to use per language.</summary>
public class ReminderSettings
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public bool IsEnabled { get; set; }
    public int FirstHoursBefore { get; set; } = 24;
    public int SecondHoursBefore { get; set; } = 2;

    /// <summary>jsonb: language code → whatsapp_templates id, e.g. {"en":"…","ar":"…"}.</summary>
    public string TemplatesJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
