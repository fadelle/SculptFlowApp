namespace PlasticSurgery.Entities.Requests.Billing;

/// <summary>A new rate version. EffectiveFrom defaults to now and can't be in the past; an existing version of the
/// same rate (card, event, country, operator, provider, provider billing) is closed at that moment. ProviderBilling
/// null = SculptFlow-funded pricing (and cost reporting for any account); a responsibility (e.g. customer_direct) =
/// a SculptFlow usage fee for accounts where the customer pays the provider.</summary>
public record AddRateRequest(
    string EventType,
    string? CountryCode,
    string? Operator,
    string? Provider,
    string? Unit,
    decimal ProviderCost,
    decimal ClientRate,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null,
    string? Notes = null,
    string? ProviderBilling = null);
