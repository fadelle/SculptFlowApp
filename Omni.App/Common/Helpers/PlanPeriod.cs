using Omni.App.Entities.Models;

namespace Omni.App.Common.Helpers;

/// <summary>When a plan's period that starts at <c>start</c> ends: a trial plan's TrialDays later, else one BillingPeriod.</summary>
public static class PlanPeriod
{
    public static DateTimeOffset End(SubscriptionPlan plan, DateTimeOffset start) =>
        plan.TrialDays is int days ? start.AddDays(days) : Common.Enums.BillingPeriod.Advance(start, plan.BillingPeriod);

    /// <summary>What a client's add-ons cost per period (units × unit price).</summary>
    public static decimal AddonsTotal(IEnumerable<ClientAddon> addons) =>
        addons.Where(a => a.Addon is not null).Sum(a => a.Quantity * a.Addon!.UnitPrice);
}
