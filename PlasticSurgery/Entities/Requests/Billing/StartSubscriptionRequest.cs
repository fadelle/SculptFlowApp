using Microsoft.Extensions.Options;

namespace PlasticSurgery.Entities.Requests.Billing;

/// <param name="IdempotencyKey">Makes a retried request (double click, network retry) a no-op. Max 150 chars.</param>
/// <param name="ChargeFirstPeriod">false = the first period is free (trial, or paid outside the wallet).</param>
public sealed record StartSubscriptionRequest(
    Guid ClinicId,
    string PlanCode,
    string IdempotencyKey,
    bool ChargeFirstPeriod,
    string Source,
    string? Actor = null,
    string? Reason = null);
