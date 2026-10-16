using Microsoft.Extensions.Options;
using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.Billing;

/// <summary>A reservation made for an outbound message, carried from before the send to after it.</summary>
public sealed record MessageBillingHold(Guid ClinicId, string IdempotencyKey, BillingSettlementTrigger SettleWhen);
