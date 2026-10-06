using System.Text.RegularExpressions;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

/// <summary>
/// The one thing the billing core is told about usage: something that consumed a communication resource, already
/// normalized by the channel that produced it. The channel's billing policy decides the event type, quantity and
/// context (country, operator, provider); the connected account's settings decide who pays the upstream provider
/// (<see cref="ProviderBilling"/>) and whether SculptFlow charges for it (<see cref="ChargesUsage"/>). Every event is
/// recorded; only charged ones are rated as a sale, reserved, settled and paid. Billing never sees a provider payload
/// or a message body.
/// </summary>
public sealed record BillableEvent
{
    public required Guid ClinicId { get; init; }

    /// <summary>Stable identity of this one billable event, unique per clinic, max 150 chars (e.g.
    /// "whatsapp:message:{messageId}"). Processing the same key twice never charges twice.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Lowercase snake_case type, e.g. whatsapp_marketing_message, sms_segment, voice_minute. Free text:
    /// a new type needs only rates, no code or schema change in billing (see <see cref="BillableEventTypes"/>).</summary>
    public required string EventType { get; init; }

    /// <summary>The channel that produced it (whatsapp, sms, email...).</summary>
    public required string Channel { get; init; }

    /// <summary>How many units (1 message, 3 SMS segments, 2.5 voice minutes, 1200 tokens...).</summary>
    public decimal Quantity { get; init; } = 1;
    public string? Unit { get; init; }

    /// <summary>ISO 3166-1 alpha-2 destination country, when the price depends on it.</summary>
    public string? CountryCode { get; init; }
    public string? Operator { get; init; }
    public string? Provider { get; init; }

    /// <summary>Who pays the upstream provider: one of <see cref="ProviderBillingResponsibility"/>. Defaults to
    /// platform_funded (SculptFlow pays and charges), the behaviour before responsibilities existed.</summary>
    public string ProviderBilling { get; init; } = ProviderBillingResponsibility.PlatformFunded;

    /// <summary>Does SculptFlow charge the clinic for this usage? Null = the default for <see cref="ProviderBilling"/>:
    /// only platform_funded usage is charged. True on a customer-paid account = a SculptFlow usage fee (needs a rate
    /// set up for that responsibility); false on platform_funded = SculptFlow absorbs the cost.</summary>
    public bool? ChargeUsage { get; init; }

    /// <summary>The connected channel account (channel_integrations.id) the usage went through, when known.</summary>
    public Guid? ChannelIntegrationId { get; init; }

    public bool ChargesUsage => ChargeUsage ?? ProviderBilling == ProviderBillingResponsibility.PlatformFunded;

    /// <summary>When the usage happened; its rate is the one effective at this moment. Defaults to now.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    public Guid? MessageId { get; init; }
    public Guid? ConversationId { get; init; }
    public Guid? CampaignId { get; init; }

    /// <summary>One of <see cref="BillingSource"/>.</summary>
    public string Source { get; init; } = BillingSource.Channel;
    public string? Actor { get; init; }
}

public enum UsageOutcome
{
    /// <summary>Funds are held; settle or release it later.</summary>
    Reserved,
    /// <summary>Charged.</summary>
    Settled,
    /// <summary>Not billable after all; the hold was returned.</summary>
    Released,
    /// <summary>Couldn't be priced or paid; nothing was charged (FailureReason says why).</summary>
    Failed,
    /// <summary>Usage recorded; SculptFlow doesn't charge it (someone else pays the provider, or there's no fee).
    /// No reservation, no wallet or credit use, no ledger row.</summary>
    Recorded,
    /// <summary>No usage record with that key (the usage wasn't billable, or billing was off when it happened).</summary>
    NotFound,
    /// <summary>The record is already final in the other direction (e.g. settle after release) — nothing changed.</summary>
    Conflict
}

/// <summary>Result of a usage operation. Duplicate = the key was already processed and the existing state is
/// returned instead of acting twice.</summary>
public sealed record UsageResult(UsageOutcome Outcome, Guid? UsageRecordId, decimal? Amount, string? FailureReason, bool Duplicate)
{
    public bool Succeeded => Outcome is UsageOutcome.Reserved or UsageOutcome.Settled or UsageOutcome.Recorded;
}

/// <summary>Result of a direct balance change (top-up, adjustment).</summary>
public sealed record LedgerResult(Guid LedgerEntryId, decimal BalanceAfter, bool Duplicate);

public sealed record WalletTopUp(Guid ClinicId, decimal Amount, string IdempotencyKey, string? Reference, string? Reason,
    string Source, string? Actor);

/// <summary>A manual correction. Reason is required. Amount is signed; BalanceType is wallet or included_credit.</summary>
public sealed record WalletAdjustment(Guid ClinicId, decimal Amount, string BalanceType, string Reason, string IdempotencyKey,
    string Source, string? Actor);

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
