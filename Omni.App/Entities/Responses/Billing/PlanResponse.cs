using Omni.App.Common.Enums;

namespace Omni.App.Entities.Responses.Billing;

public record PlanResponse(Guid Id, string Code, string Name, string? Description, decimal Price, string Currency, string BillingPeriod,
    decimal IncludedUsageCredit, string? RateCardCode, bool IsActive, int SortOrder, IReadOnlyDictionary<string, string> Entitlements,
    int Subscribers);
