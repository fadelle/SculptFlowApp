namespace Omni.App.Common.Enums;

/// <summary>Allowed values for scheduling.booking_reminders.status (ck_booking_reminders_status). "sending" is the claim
/// written before the send, so two workers never send the same reminder; it then becomes sent, skipped or failed.</summary>
public static class ReminderStatus
{
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}
