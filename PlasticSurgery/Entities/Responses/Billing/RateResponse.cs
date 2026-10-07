namespace PlasticSurgery.Entities.Responses.Billing;

public record RateResponse(Guid Id, string RateCardCode, string EventType, string? CountryCode, string? Operator, string? Provider,
    string? ProviderBilling, string Unit, decimal ProviderCost, decimal ClientRate, decimal Margin, string Currency,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string? Notes, string? CreatedBy, DateTimeOffset CreatedAt);
