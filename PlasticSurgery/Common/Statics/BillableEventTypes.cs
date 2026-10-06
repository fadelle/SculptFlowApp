using System.Text.RegularExpressions;

namespace PlasticSurgery.Common.Statics;

/// <summary>Well-known billable event types and their clinic-facing labels. NOT an allow-list: any lowercase
/// snake_case type works as soon as a rate card prices it. Add a constant here only so code and labels share it.</summary>
public static partial class BillableEventTypes
{
    public const string WhatsAppMarketingMessage = "whatsapp_marketing_message";
    public const string WhatsAppUtilityMessage = "whatsapp_utility_message";
    public const string WhatsAppAuthenticationMessage = "whatsapp_authentication_message";
    public const string WhatsAppServiceMessage = "whatsapp_service_message";
    public const string TelegramMessage = "telegram_message";

    // Not produced by any channel yet — listed so future channels and rate cards use the same names.
    public const string SmsSegment = "sms_segment";
    public const string ViberTransactionalMessage = "viber_transactional_message";
    public const string ViberPromotionalMessage = "viber_promotional_message";
    public const string EmailRecipient = "email_recipient";
    public const string RcsMessage = "rcs_message";
    public const string VoiceMinute = "voice_minute";
    public const string AiToken = "ai_token";
    public const string WhatsAppNumberMonth = "whatsapp_number_month";
    public const string AgentSeatMonth = "agent_seat_month";

    private static readonly Dictionary<string, string> Labels = new()
    {
        [WhatsAppMarketingMessage] = "WhatsApp marketing message",
        [WhatsAppUtilityMessage] = "WhatsApp utility message",
        [WhatsAppAuthenticationMessage] = "WhatsApp authentication message",
        [WhatsAppServiceMessage] = "WhatsApp service message",
        [TelegramMessage] = "Telegram message",
        [SmsSegment] = "SMS segment",
        [ViberTransactionalMessage] = "Viber transactional message",
        [ViberPromotionalMessage] = "Viber promotional message",
        [EmailRecipient] = "Email recipient",
        [RcsMessage] = "RCS message",
        [VoiceMinute] = "Voice minute",
        [AiToken] = "AI token",
        [WhatsAppNumberMonth] = "WhatsApp number (monthly)",
        [AgentSeatMonth] = "Agent seat (monthly)",
    };

    public static bool IsValid(string? eventType) =>
        !string.IsNullOrEmpty(eventType) && eventType.Length <= 60 && ValidPattern().IsMatch(eventType);

    /// <summary>Human label for UI; unknown types are humanized ("viber_session" -> "Viber session").</summary>
    public static string Label(string eventType)
    {
        if (Labels.TryGetValue(eventType, out var label)) return label;
        var words = eventType.Replace('_', ' ').Trim();
        if (words.Length == 0) return eventType;
        words = words.Replace("whatsapp", "WhatsApp").Replace("sms", "SMS").Replace("rcs", "RCS").Replace(" ai ", " AI ");
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ValidPattern();
}
