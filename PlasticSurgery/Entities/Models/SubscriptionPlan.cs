using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>A plan a clinic can subscribe to (Starter, Growth, Business...). It decides the recurring price, the
/// entitlements (features and limits, see <see cref="SubscriptionPlanEntitlement"/>) and an optional monetary usage
/// credit granted each period. Usage itself is never a quota here: it is charged per billable event (see
/// BillingUsageRecord). Price changes apply from the next renewal. See Billing/ISubscriptionService.cs.</summary>
public class SubscriptionPlan
{
    public Guid Id { get; set; }
    /// <summary>Stable lowercase identifier (e.g. "growth"), used by config (Billing:SignupPlanCode) and the admin API.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>One of <see cref="BillingPeriod"/>.</summary>
    public string BillingPeriod { get; set; } = Common.Enums.BillingPeriod.Month;
    /// <summary>Monetary credit granted at each period start, spent before the wallet. Unused credit expires at renewal.</summary>
    public decimal IncludedUsageCredit { get; set; }
    /// <summary>Optional plan price list; rating falls back to the default rate card when it has no matching rate.</summary>
    public Guid? RateCardId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SubscriptionPlanEntitlement> Entitlements { get; set; } = new List<SubscriptionPlanEntitlement>();
}
