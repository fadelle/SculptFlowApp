using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Engines.Billing;

/// <summary>
/// A channel's billing rules — the ONLY place that knows how a channel/provider charges. Billing itself stays
/// generic. One implementation per channel (Business/Engines/Billing/WhatsAppBillingPolicy.cs,
/// Business/Engines/Billing/TelegramBillingPolicy.cs); a new channel adds one and registers it as IChannelBillingPolicy.
/// A channel with no policy is treated as free (and logged).
/// </summary>
public interface IChannelBillingPolicy
{
    /// <summary>The ConversationChannel value this policy covers.</summary>
    string Channel { get; }

    /// <summary>Before a send: is it billable, as what, how many units? null = not billable (no reservation).</summary>
    ChannelBillingDecision? DescribeOutbound(OutboundMessageBillingContext message);

    /// <summary>A delivery status arrived for a message that was reserved: settle, release, or wait.</summary>
    DeliveryBillingAction OnDeliveryStatus(string status);

    /// <summary>The outcome never arrived before the reservation timeout: decide from what the message row shows
    /// now (null when the message row doesn't exist, e.g. the app stopped right after the send).</summary>
    DeliveryBillingAction ResolveStale(Message? message);
}
