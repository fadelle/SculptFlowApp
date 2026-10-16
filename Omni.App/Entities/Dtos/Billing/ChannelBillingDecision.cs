using Microsoft.Extensions.Options;
using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.Billing;

/// <summary>The channel's verdict for one outbound message: it is billable as this event.</summary>
public sealed record ChannelBillingDecision(
    string EventType,
    decimal Quantity,
    string Unit,
    string? CountryCode,
    string? Operator,
    string? Provider,
    BillingSettlementTrigger SettleWhen);
