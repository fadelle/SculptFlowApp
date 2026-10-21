namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>An approved WhatsApp template the reminder settings can pick.</summary>
public record ReminderTemplateOption(Guid Id, string Name, string Language);
