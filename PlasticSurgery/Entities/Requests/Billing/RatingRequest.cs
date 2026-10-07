using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Requests.Billing;

/// <summary>What is being priced.</summary>
/// <param name="ProviderBilling">Who pays the upstream provider (ProviderBillingResponsibility).</param>
/// <param name="ForCharging">true = SculptFlow will charge this price. Then a customer-paid / external / no-fee
/// account only matches rates set for its own responsibility (a SculptFlow usage fee) — never a platform rate by
/// fallback. false = the lookup is only for provider-cost reporting, and the generic rate's cost is fine.</param>
public sealed record RatingRequest(Guid ClinicId, string EventType, string? CountryCode, string? Operator, string? Provider,
    DateTimeOffset At, string ProviderBilling = ProviderBillingResponsibility.PlatformFunded, bool ForCharging = true);
