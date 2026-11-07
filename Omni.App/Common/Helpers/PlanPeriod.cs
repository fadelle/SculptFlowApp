using Omni.App.Entities.Models;

namespace Omni.App.Common.Helpers;

/// <summary>When a plan's period that starts at <c>start</c> ends: a trial plan's TrialDays later, else one BillingPeriod.</summary>
public static class PlanPeriod
{
    public static DateTimeOffset End(SubscriptionPlan plan, DateTimeOffset start) => End(plan, start, start);

    /// <summary>When the period that starts at <paramref name="start"/> ends, counted from <paramref name="anchor"/> (the start of the run
    /// of periods): the first anchor + k periods after start. Counting from the anchor, never from the last end, keeps the day: a
    /// month anchored on the 31st ends on Sep 30, then Oct 31 (QA t2-billing-19). A trial ends TrialDays after start.</summary>
    public static DateTimeOffset End(SubscriptionPlan plan, DateTimeOffset anchor, DateTimeOffset start)
    {
        if (plan.TrialDays is int days) return start.AddDays(days);
        var yearly = plan.BillingPeriod == Common.Enums.BillingPeriod.Year;
        // Periods whole before start's month (or year) end before start, so counting starts there.
        var k = Math.Max(0, yearly ? start.Year - anchor.Year : (start.Year - anchor.Year) * 12 + start.Month - anchor.Month);
        for (; ; k++)
        {
            var end = yearly ? anchor.AddYears(k) : anchor.AddMonths(k);
            if (end > start) return end;
        }
    }

    /// <summary>What a client's add-ons cost per period (units × unit price).</summary>
    public static decimal AddonsTotal(IEnumerable<ClientAddon> addons) =>
        addons.Where(a => a.Addon is not null).Sum(a => a.Quantity * a.Addon!.UnitPrice);
}
