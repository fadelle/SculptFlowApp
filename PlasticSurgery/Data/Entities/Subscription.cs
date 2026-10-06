namespace PlasticSurgery.Data.Entities;

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
    public string BillingPeriod { get; set; } = Entities.BillingPeriod.Month;
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

/// <summary>One entitlement of a plan. Keys are defined in code (Billing/Entitlements.cs). Value is "true"/"false"
/// for a feature and a number or "unlimited" for a limit. A key the plan doesn't list is off / 0.</summary>
public class SubscriptionPlanEntitlement
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string EntitlementKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The clinic's one subscription (unique per clinic). Changing plan updates this row; the history of
/// changes is in the events table and the financial side in billing.ledger_entries.</summary>
public class ClinicSubscription
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>One of <see cref="SubscriptionStatus"/>.</summary>
    public string Status { get; set; } = SubscriptionStatus.Active;
    public DateTimeOffset CurrentPeriodStart { get; set; }
    public DateTimeOffset CurrentPeriodEnd { get; set; }
    /// <summary>Cancel requested: the subscription ends (cancelled) at CurrentPeriodEnd instead of renewing.</summary>
    public bool CancelAtPeriodEnd { get; set; }
    /// <summary>When a renewal first failed for lack of funds; the grace period counts from here.</summary>
    public DateTimeOffset? PastDueSince { get; set; }
    /// <summary>When the subscription became expired or cancelled.</summary>
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SubscriptionPlan? Plan { get; set; }
}

/// <summary>Allowed values for ClinicSubscription.Status — must match schema.sql's CHECK constraint.</summary>
public static class SubscriptionStatus
{
    /// <summary>Paid for the current period.</summary>
    public const string Active = "active";
    /// <summary>The renewal couldn't be paid; the plan stays usable until the grace period (Billing:GracePeriodDays) ends.</summary>
    public const string PastDue = "past_due";
    /// <summary>The grace period ran out without payment. Data stays viewable; sending, campaigns and the AI stop.</summary>
    public const string Expired = "expired";
    /// <summary>Ended on request. Same restrictions as expired.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>Allowed values for SubscriptionPlan.BillingPeriod.</summary>
public static class BillingPeriod
{
    public const string Month = "month";
    public const string Year = "year";

    public static bool IsValid(string? value) => value is Month or Year;

    /// <summary>The end of one period that starts at <paramref name="start"/>.</summary>
    public static DateTimeOffset Advance(DateTimeOffset start, string period) =>
        period == Year ? start.AddYears(1) : start.AddMonths(1);
}
