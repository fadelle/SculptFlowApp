using Omni.App.Common.Enums;

namespace Omni.App.Entities.Models;

/// <summary>The client's one subscription (unique per client). Changing plan updates this row; the history of
/// changes is in the events table and the financial side in billing.ledger_entries.</summary>
public class ClientSubscription
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
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
