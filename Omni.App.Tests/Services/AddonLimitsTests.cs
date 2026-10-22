namespace Omni.App.Tests.Services;

public class AddonLimitsTests
{
    private static ClientAddon Units(string key, int quantity, decimal price = 5m) =>
        new() { Quantity = quantity, Addon = new Addon { Code = key, EntitlementKey = key, UnitPrice = price, Currency = "USD" } };

    [Fact]
    public void Add_on_units_raise_the_plans_limits_and_leave_features_and_unlimited_alone()
    {
        var plan = new Dictionary<string, string>
        {
            [EntitlementKeys.MaxUsers] = "5",
            [EntitlementKeys.MaxChannelConnections] = EntitlementCatalog.Unlimited,
            [EntitlementKeys.Campaigns] = "true",
        };

        var merged = AddonLimits.Merge(plan, [Units(EntitlementKeys.MaxUsers, 2), Units(EntitlementKeys.MaxChannelConnections, 3),
            Units(EntitlementKeys.MaxAiBots, 1), Units(EntitlementKeys.MaxWhatsAppNumbers, 0)]);

        Assert.Equal("7", merged[EntitlementKeys.MaxUsers]);
        Assert.Equal(EntitlementCatalog.Unlimited, merged[EntitlementKeys.MaxChannelConnections]);
        Assert.Equal("1", merged[EntitlementKeys.MaxAiBots]);       // the plan didn't list it: 0 + 1
        Assert.False(merged.ContainsKey(EntitlementKeys.MaxWhatsAppNumbers)); // 0 units change nothing
        Assert.Equal("true", merged[EntitlementKeys.Campaigns]);
        Assert.Equal("5", plan[EntitlementKeys.MaxUsers]);            // the plan's own values are untouched
    }

    [Fact]
    public void A_trial_plan_ends_after_its_days_and_a_paid_plan_after_its_period()
    {
        var start = new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(start.AddDays(7), PlanPeriod.End(new SubscriptionPlan { BillingPeriod = BillingPeriod.Month, TrialDays = 7 }, start));
        Assert.Equal(start.AddMonths(1), PlanPeriod.End(new SubscriptionPlan { BillingPeriod = BillingPeriod.Month }, start));
        Assert.Equal(start.AddYears(1), PlanPeriod.End(new SubscriptionPlan { BillingPeriod = BillingPeriod.Year }, start));
        Assert.Equal(31m, PlanPeriod.AddonsTotal([Units("a", 2, 8m), Units("b", 1, 15m)]));
    }
}
