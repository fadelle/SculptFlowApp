using PlasticSurgery.Business.Contracts.Engines.Billing;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Engines.Billing;

/// <summary>Telegram's Bot API has no per-message fee. Outbound messages are still billable EVENTS
/// (<c>telegram_message</c>) so usage is counted like any channel's, but the account's default arrangement is
/// no_provider_usage_fee, so nothing is rated or charged unless an admin turns on a SculptFlow usage fee for it.
/// The send itself is the outcome (Telegram reports no delivery statuses).</summary>
public class TelegramBillingPolicy : IChannelBillingPolicy
{
    public string Channel => ConversationChannel.Telegram;

    public ChannelBillingDecision? DescribeOutbound(OutboundMessageBillingContext message) =>
        new(BillableEventTypes.TelegramMessage, 1, "message", CountryCode: null, Operator: null, Provider: null,
            BillingSettlementTrigger.OnProviderAccepted);

    public DeliveryBillingAction OnDeliveryStatus(string status) => DeliveryBillingAction.None;

    public DeliveryBillingAction ResolveStale(Message? message) =>
        message is null ? DeliveryBillingAction.Release : DeliveryBillingAction.Settle;
}
