using Omni.App.Common.Enums;

namespace Omni.App.Entities.Responses.Billing;

public record QuoteResponse(string EventType, string? CountryCode, string? Operator, string? Provider, Guid RateId, string RateCardCode,
    string RateSource, decimal UnitPrice, decimal UnitProviderCost, string Currency, string Unit);
