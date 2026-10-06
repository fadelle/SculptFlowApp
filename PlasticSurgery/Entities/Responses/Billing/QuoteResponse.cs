using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Responses.Billing;

public record QuoteResponse(string EventType, string? CountryCode, string? Operator, string? Provider, Guid RateId, string RateCardCode,
    string RateSource, decimal UnitPrice, decimal UnitProviderCost, string Currency, string Unit);
