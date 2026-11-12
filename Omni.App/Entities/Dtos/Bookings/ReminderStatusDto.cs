namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>Booking reminders at a glance (QA t5-ops-07): whether they are on, what can carry them (WhatsApp needs a chosen
/// template; Telegram sends text) and what they did in the last 7 days.</summary>
public sealed record ReminderStatusDto(bool Enabled, bool WhatsAppConnected, int TemplateCount, bool TelegramConnected,
    int Sent7Days, int Skipped7Days, int Failed7Days)
{
    /// <summary>At least one channel could send a reminder now.</summary>
    public bool CanSendAny => TelegramConnected || (WhatsAppConnected && TemplateCount > 0);
}
