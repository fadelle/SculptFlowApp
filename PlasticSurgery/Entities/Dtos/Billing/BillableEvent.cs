using System.Text.RegularExpressions;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

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
