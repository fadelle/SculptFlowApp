using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>The price found for one billable event: per-unit client rate and provider cost, plus where it came from.</summary>
public sealed record RateQuote(Guid RateId, Guid RateCardId, string RateSource, decimal UnitPrice, decimal UnitProviderCost,
    string Currency, string Unit);
