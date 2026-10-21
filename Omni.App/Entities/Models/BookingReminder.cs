namespace Omni.App.Entities.Models;

/// <summary>One reminder for one booking start time (scheduling.booking_reminders): written as "sending" before the send
/// (unique per booking, kind and start), then sent, skipped or failed with a ReminderReason.</summary>
public class BookingReminder
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid BookingId { get; set; }

    /// <summary>ReminderKind value.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The booking's start when the reminder was due (a reschedule gets its own rows).</summary>
    public DateTimeOffset ScheduledStart { get; set; }

    /// <summary>ReminderStatus value.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>ReminderReason value, for skipped and failed.</summary>
    public string? Reason { get; set; }

    public string? Channel { get; set; }
    public Guid? MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
