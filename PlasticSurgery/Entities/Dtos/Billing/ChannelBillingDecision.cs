using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>The channel's verdict for one outbound message: it is billable as this event.</summary>
public sealed record ChannelBillingDecision(
    string EventType,
    decimal Quantity,
    string Unit,
    string? CountryCode,
    string? Operator,
    string? Provider,
    BillingSettlementTrigger SettleWhen);
