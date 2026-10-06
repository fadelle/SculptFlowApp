using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>A reservation made for an outbound message, carried from before the send to after it.</summary>
public sealed record MessageBillingHold(Guid ClinicId, string IdempotencyKey, BillingSettlementTrigger SettleWhen);
