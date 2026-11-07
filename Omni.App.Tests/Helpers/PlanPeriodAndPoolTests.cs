using Omni.App.Persistence.Helpers;

namespace Omni.App.Tests.Helpers;

public class PlanPeriodAndPoolTests
{
    private static SubscriptionPlan Plan(string period = BillingPeriod.Month, int? trialDays = null) =>
        new() { Code = "p", Name = "P", BillingPeriod = period, TrialDays = trialDays };

    private static DateTimeOffset Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, TimeSpan.Zero);

    // QA t2-billing-19: counted from the last end, a month started on the 31st moved to the 30th (or the 28th) for good.
    [Theory]
    [InlineData(2027, 1, 31, 2027, 2, 28, 2027, 3, 31)]
    [InlineData(2027, 1, 31, 2027, 4, 30, 2027, 5, 31)]
    [InlineData(2027, 1, 30, 2027, 2, 28, 2027, 3, 30)]
    [InlineData(2027, 1, 15, 2027, 1, 15, 2027, 2, 15)]
    [InlineData(2027, 1, 31, 2027, 3, 10, 2027, 3, 31)] // a start between boundaries ends at the next one
    public void A_month_ends_a_whole_number_of_months_after_the_anchor(int ay, int am, int ad, int sy, int sm, int sd, int ey, int em, int ed) =>
        Assert.Equal(Utc(ey, em, ed), PlanPeriod.End(Plan(), Utc(ay, am, ad), Utc(sy, sm, sd)));

    [Fact]
    public void A_year_keeps_the_29th_of_february_when_it_can_and_a_trial_counts_its_days()
    {
        Assert.Equal(Utc(2029, 2, 28), PlanPeriod.End(Plan(BillingPeriod.Year), Utc(2028, 2, 29), Utc(2028, 2, 29)));
        Assert.Equal(Utc(2032, 2, 29), PlanPeriod.End(Plan(BillingPeriod.Year), Utc(2028, 2, 29), Utc(2031, 2, 28)));
        Assert.Equal(Utc(2027, 1, 8), PlanPeriod.End(Plan(trialDays: 7), Utc(2026, 12, 1), Utc(2027, 1, 1)));
        Assert.Equal(Utc(2027, 2, 28), PlanPeriod.End(Plan(), Utc(2027, 1, 31))); // without an anchor: the start
    }

    // QA t3-inbox-02: Npgsql's default of 100 connections let a burst of webhooks exceed the Supabase pooler's limit (15).
    [Fact]
    public void The_pool_cap_is_added_unless_the_connection_string_sets_one()
    {
        var capped = ConnectionPool.WithPoolLimit("Host=db;Username=u;Password=p", 10);
        Assert.Contains("Maximum Pool Size=10", capped);
        Assert.Contains("Host=db", capped);
        Assert.Equal("Host=db;Max Pool Size=4", ConnectionPool.WithPoolLimit("Host=db;Max Pool Size=4", 10));
        Assert.Equal("Host=db;maximum pool size=4", ConnectionPool.WithPoolLimit("Host=db;maximum pool size=4", 10));
        Assert.Equal("Host=db", ConnectionPool.WithPoolLimit("Host=db", null));
        Assert.Throws<InvalidOperationException>(() => ConnectionPool.WithPoolLimit("Host=db", 0));
    }
}
