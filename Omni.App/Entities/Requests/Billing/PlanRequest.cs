using Omni.App.Common.Enums;

namespace Omni.App.Entities.Requests.Billing;

/// <summary>A plan to create (POST) or the new values of one (PUT). Name, Price and BillingPeriod are always required (a PUT
/// without a price would make the plan free). IncludedUsageCredit, IsActive, SortOrder and Entitlements left out (null) keep the
/// plan's value on a PUT, or are 0 / true / 0 / none for a new plan (QA t2-billing-17: a PUT without isActive reactivated a
/// plan). TrialDays null = a normal plan; a plan with subscribers can't switch between trial and paid. Description and
/// RateCardCode null = none.</summary>
public record PlanRequest(
    string Code,
    string Name,
    string? Description,
    decimal? Price,
    string BillingPeriod,
    decimal? IncludedUsageCredit,
    string? RateCardCode,
    bool? IsActive = null,
    int? SortOrder = null,
    Dictionary<string, string>? Entitlements = null,
    int? TrialDays = null);
