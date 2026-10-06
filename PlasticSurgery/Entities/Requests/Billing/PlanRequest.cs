using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Requests.Billing;

public record PlanRequest(
    string Code,
    string Name,
    string? Description,
    decimal Price,
    string BillingPeriod,
    decimal IncludedUsageCredit,
    string? RateCardCode,
    bool IsActive = true,
    int SortOrder = 0,
    Dictionary<string, string>? Entitlements = null);
