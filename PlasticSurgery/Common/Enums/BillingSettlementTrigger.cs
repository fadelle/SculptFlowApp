using Microsoft.Extensions.Options;

namespace PlasticSurgery.Common.Enums;

/// <summary>When reserved usage of a channel becomes billable.</summary>
public enum BillingSettlementTrigger
{
    /// <summary>When the provider confirms the outcome later (e.g. WhatsApp "delivered").</summary>
    OnDeliveryStatus,
    /// <summary>As soon as the provider accepts the send (e.g. a channel billed per submission).</summary>
    OnProviderAccepted
}
