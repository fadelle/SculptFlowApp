using Microsoft.Extensions.Options;

namespace Omni.App.Entities.Requests.Billing;

/// <param name="IdempotencyKey">Makes a retried request (double click, network retry) a no-op. Max 150 chars.</param>
/// <param name="ChargeFirstPeriod">false = the first period is free (trial, or paid outside the wallet).</param>
/// <param name="AllowAnotherTrial">A client gets one trial (QA t2-billing-21); true lets the admin give another on purpose.</param>
public sealed record StartSubscriptionRequest(
    Guid ClientId,
    string PlanCode,
    string IdempotencyKey,
    bool ChargeFirstPeriod,
    string Source,
    string? Actor = null,
    string? Reason = null,
    bool AllowAnotherTrial = false);
