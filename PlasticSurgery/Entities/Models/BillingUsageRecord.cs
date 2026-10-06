using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>One billable event (a CDR): what happened, who paid the provider for it, and what SculptFlow charged.
/// Recorded whether or not SculptFlow charges it. Two separate statuses:
///   ChargeStatus    (SculptFlow's money): reserved -> settled | released; failed = refused (no rate / not enough
///                   balance); not_charged = SculptFlow doesn't bill this usage — never touches wallet, credit or ledger.
///   ProviderOutcome (what happened at the provider): pending -> billable (e.g. delivered) | not_billable (failed).
/// Prices are snapshotted at rating time. A DB trigger stops a final record from changing except for its refund
/// columns and provider outcome.</summary>
public class BillingUsageRecord
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid BillingAccountId { get; set; }
    /// <summary>Stable identity of this one billable event (unique per clinic), e.g. "whatsapp:message:{messageId}".
    /// A retry or a duplicate callback with the same key finds this row instead of charging again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    /// <summary>The connected channel account (channel_integrations.id) the usage went through, when known.</summary>
    public Guid? ChannelIntegrationId { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public string? CountryCode { get; set; }
    public string? Operator { get; set; }
    public string? Provider { get; set; }
    /// <summary>Who paid the upstream provider: one of <see cref="ProviderBillingResponsibility"/> (snapshot).</summary>
    public string ProviderBilling { get; set; } = ProviderBillingResponsibility.PlatformFunded;

    public Guid? RateId { get; set; }
    public Guid? RateCardId { get; set; }
    /// <summary>Which level of the rate-card hierarchy priced it: one of <see cref="RateSource"/>.</summary>
    public string? RateSource { get; set; }
    public decimal? UnitProviderCost { get; set; }
    public decimal? UnitPrice { get; set; }
    /// <summary>Total upstream provider cost (UnitProviderCost x Quantity) when known, whoever paid it. On
    /// SculptFlow-funded usage, Amount - this = margin.</summary>
    public decimal? ProviderCost { get; set; }
    /// <summary>Total SculptFlow charges the clinic (UnitPrice x Quantity): the estimate while reserved, final once
    /// settled, 0 when not charged.</summary>
    public decimal? Amount { get; set; }
    public decimal ReservedAmount { get; set; }
    /// <summary>Settled amount paid from included credit.</summary>
    public decimal CreditAmount { get; set; }
    /// <summary>Settled amount paid from the wallet.</summary>
    public decimal WalletAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>SculptFlow's money side: one of <see cref="ChargeStatus"/>.</summary>
    public string ChargeStatus { get; set; } = Common.Enums.ChargeStatus.Reserved;
    /// <summary>The provider side: one of <see cref="ProviderOutcome"/>.</summary>
    public string ProviderOutcome { get; set; } = Common.Enums.ProviderOutcome.Pending;
    /// <summary>For failed records: one of <see cref="UsageFailureReason"/>.</summary>
    public string? FailureReason { get; set; }
    public string? ReleaseReason { get; set; }

    public Guid? MessageId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? CampaignId { get; set; }
    /// <summary>One of <see cref="BillingSource"/>.</summary>
    public string Source { get; set; } = BillingSource.Channel;
    public string? Actor { get; set; }

    /// <summary>When the billable event happened — the date its rate was looked up for.</summary>
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ReservedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
