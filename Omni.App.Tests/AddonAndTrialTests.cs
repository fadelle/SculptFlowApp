using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Services.Billing;
using Omni.App.Entities.Requests.Billing;

namespace Omni.App.Tests;

/// <summary>Milestone C pricing: trial plans that end instead of renewing, and paid add-ons (catalog, charging, renewal,
/// limits).</summary>
[Collection("Postgres")]
public class AddonAndTrialTests
{
    private readonly PostgresFixture _db;

    public AddonAndTrialTests(PostgresFixture db) => _db = db;

    private static StartSubscriptionRequest Start(Guid client, string plan, bool charge = true) =>
        new(client, plan, Guid.NewGuid().ToString("N"), charge, BillingSource.Admin, "ops@omni", "test");

    private static AddonService Addons(BillingHarness h) =>
        new(h.Factory, h.Config, h.Cache, h.Time, NullLogger<AddonService>.Instance);

    private static async Task<string> TrialPlanAsync(BillingHarness h, int days = 7)
    {
        var code = BillingHarness.Unique("trial").Replace('_', '-');
        await h.Plans.CreateAsync(new PlanRequest(code, "Free Trial", null, 0m, "month", 2m, null, true, 0,
            new Dictionary<string, string> { [EntitlementKeys.MaxUsers] = "1", [EntitlementKeys.AiAssistant] = "true" }, TrialDays: days));
        return code;
    }

    /// <summary>An add-on with a unique code and entitlement-free catalog slot: the tests share one database, so each makes
    /// its own add-on on a key no other add-on uses (the seed may already hold max_users etc.).</summary>
    private static async Task<string> AddonAsync(BillingHarness h, string key, decimal price)
    {
        await using (var db = h.Db())
        {
            // One add-on per limit key: drop leftovers on this key from earlier runs (and their client rows).
            var old = await db.Addons.Where(a => a.EntitlementKey == key).Select(a => a.Id).ToListAsync();
            await db.ClientAddons.Where(c => old.Contains(c.AddonId)).ExecuteDeleteAsync();
            await db.Addons.Where(a => old.Contains(a.Id)).ExecuteDeleteAsync();
        }
        var code = BillingHarness.Unique("addon").Replace('_', '-');
        await Addons(h).SaveAsync(code, new AddonRequest("Extra " + key, key, price));
        return code;
    }

    [PostgresFact]
    public async Task A_trial_plan_must_be_free_and_last_1_to_90_days()
    {
        var h = new BillingHarness(_db.ConnectionString);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Plans.CreateAsync(new PlanRequest("t-paid", "Paid trial", null, 5m, "month", 0m, null, TrialDays: 7)));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Plans.CreateAsync(new PlanRequest("t-long", "Long trial", null, 0m, "month", 0m, null, TrialDays: 91)));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Plans.CreateAsync(new PlanRequest("t-zero", "Zero trial", null, 0m, "month", 0m, null, TrialDays: 0)));

        var code = await TrialPlanAsync(h, 14);
        Assert.Equal(14, (await h.Plans.GetAsync(code))!.TrialDays);
    }

    [PostgresFact]
    public async Task A_trial_runs_for_its_days_then_expires_instead_of_renewing()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await TrialPlanAsync(h);
        await h.TopUpAsync(client, 500m); // money in the wallet must not turn a trial into a paid renewal

        var subscription = await h.Subscriptions.StartAsync(Start(client, plan, charge: false));
        Assert.Equal(h.Time.Now.AddDays(7), subscription.CurrentPeriodEnd);
        Assert.Equal(2m, (await h.AccountAsync(client)).IncludedCreditBalance);

        h.Time.Advance(TimeSpan.FromDays(6));
        Assert.Equal(RenewalOutcome.NotDue, await h.Subscriptions.RenewIfDueAsync(client));

        h.Time.Advance(TimeSpan.FromDays(1.5));
        Assert.Equal(RenewalOutcome.Expired, await h.Subscriptions.RenewIfDueAsync(client));

        var ended = (await h.Subscriptions.GetAsync(client))!;
        Assert.Equal((SubscriptionStatus.Expired, subscription.CurrentPeriodEnd), (ended.Status, ended.EndedAt));
        var account = await h.AccountAsync(client);
        Assert.Equal((500m, 0m), (account.WalletBalance, account.IncludedCreditBalance));
        Assert.False((await h.Entitlements().GetAsync(client)).HasAccess);
        await using (var db = h.Db())
        {
            var expired = await db.Events.Where(e => e.ClientId == client && e.EventType == BillingEventTypes.SubscriptionExpired).ToListAsync();
            Assert.Contains("trialEnded", Assert.Single(expired).Metadata);
        }
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task Adding_units_charges_them_now_raises_the_limit_and_a_retry_charges_once()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(39m, 0m, new Dictionary<string, string> { [EntitlementKeys.MaxUsers] = "2" });
        var addon = await AddonAsync(h, EntitlementKeys.MaxUsers, 8m);
        await h.TopUpAsync(client, 100m);
        await h.Subscriptions.StartAsync(Start(client, plan));
        Assert.Equal(2, (await h.Entitlements().GetAsync(client)).MaximumUsers);

        var rows = await Addons(h).SetQuantityAsync(client, addon, 3, "k-add", BillingSource.Admin, "ops@omni");
        var again = await Addons(h).SetQuantityAsync(client, addon, 3, "k-add", BillingSource.Admin, "ops@omni");

        Assert.Equal((3, 24m), (Assert.Single(rows).Quantity, rows[0].PeriodPrice));
        Assert.Single(again);
        Assert.Equal(100m - 39m - 24m, (await h.AccountAsync(client)).WalletBalance);
        var charge = Assert.Single(await h.LedgerAsync(client), l => l.EntryType == LedgerEntryType.AddonCharge);
        Assert.Equal((-24m, "ops@omni"), (charge.Amount, charge.Actor));
        Assert.Equal(5, (await h.Entitlements().GetAsync(client)).MaximumUsers); // cache cleared: 2 + 3

        // Fewer units refund nothing; 0 removes the row.
        await Addons(h).SetQuantityAsync(client, addon, 1, "k-less", BillingSource.Admin, "ops@omni");
        Assert.Equal(37m, (await h.AccountAsync(client)).WalletBalance);
        Assert.Empty(await Addons(h).SetQuantityAsync(client, addon, 0, "k-none", BillingSource.Admin, "ops@omni"));
        Assert.Equal(2, (await h.Entitlements().GetAsync(client)).MaximumUsers);
        await using (var db = h.Db())
        {
            Assert.Equal(3, await db.Events.CountAsync(e => e.ClientId == client && e.EventType == BillingEventTypes.AddonChanged));
        }
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task Units_the_wallet_cant_pay_or_without_a_subscription_are_refused()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var addon = await AddonAsync(h, EntitlementKeys.MaxAiBots, 15m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Addons(h).SetQuantityAsync(client, addon, 1, "k1", BillingSource.Admin, null));

        var plan = await h.CreatePlanAsync(10m, 0m, new Dictionary<string, string> { [EntitlementKeys.MaxAiBots] = "1" });
        await h.TopUpAsync(client, 20m);
        await h.Subscriptions.StartAsync(Start(client, plan));
        await Assert.ThrowsAsync<BillingDeniedException>(() =>
            Addons(h).SetQuantityAsync(client, addon, 1, "k2", BillingSource.Admin, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Addons(h).SetQuantityAsync(client, "no-such-addon", 1, "k3", BillingSource.Admin, null));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Addons(h).SetQuantityAsync(client, addon, -1, "k4", BillingSource.Admin, null));

        Assert.Empty(await Addons(h).ListForClientAsync(client));
        Assert.Equal(10m, (await h.AccountAsync(client)).WalletBalance);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task A_renewal_charges_the_plan_and_the_add_ons_and_goes_past_due_when_the_wallet_cant_pay_both()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(39m, 0m, new Dictionary<string, string> { [EntitlementKeys.MaxChannelConnections] = "2" });
        var addon = await AddonAsync(h, EntitlementKeys.MaxChannelConnections, 5m);
        await h.TopUpAsync(client, 39m + 10m + 39m + 10m);
        await h.Subscriptions.StartAsync(Start(client, plan));
        await Addons(h).SetQuantityAsync(client, addon, 2, "k-add", BillingSource.Admin, null);

        h.Time.Advance(TimeSpan.FromDays(32));
        Assert.Equal(RenewalOutcome.Renewed, await h.Subscriptions.RenewIfDueAsync(client));
        Assert.Equal(0m, (await h.AccountAsync(client)).WalletBalance);
        Assert.Equal(2, (await h.LedgerAsync(client)).Count(l => l.EntryType == LedgerEntryType.AddonCharge && l.Amount == -10m));

        // Next period: 39 alone would be payable after a 40 top-up, but not 39 + 10.
        await h.TopUpAsync(client, 40m);
        h.Time.Advance(TimeSpan.FromDays(32));
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(client));
        Assert.Equal(40m, (await h.AccountAsync(client)).WalletBalance);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task The_catalog_keeps_one_add_on_per_limit_and_its_limit_fixed()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var addon = await AddonAsync(h, EntitlementKeys.MaxWhatsAppNumbers, 10m);
        var service = Addons(h);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync("other-numbers", new AddonRequest("More numbers", EntitlementKeys.MaxWhatsAppNumbers, 9m)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(addon, new AddonRequest("Numbers", EntitlementKeys.MaxUsers, 9m)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync("x-campaigns", new AddonRequest("Campaigns", EntitlementKeys.Campaigns, 9m)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync("Bad Code", new AddonRequest("Bad", EntitlementKeys.MaxAiBots, 9m)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(addon, new AddonRequest("Numbers", null, 9.999m)));

        var saved = await service.SaveAsync(addon, new AddonRequest("WhatsApp number", null, 12m, IsActive: false));
        Assert.Equal((EntitlementKeys.MaxWhatsAppNumbers, 12m, false, 0), (saved.EntitlementKey, saved.UnitPrice, saved.IsActive, saved.Clients));
    }

    [PostgresFact]
    public async Task The_billing_summary_shows_the_add_ons_and_the_raised_limits()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await TrialPlanAsync(h);
        var addon = await AddonAsync(h, EntitlementKeys.MaxUsers, 0m); // a free add-on works on a trial with an empty wallet
        await h.Subscriptions.StartAsync(Start(client, plan, charge: false));
        await Addons(h).SetQuantityAsync(client, addon, 2, "k-add", BillingSource.Admin, null);

        var summary = (await h.Queries.GetSummaryAsync(client))!;

        Assert.Equal(7, summary.TrialDays);
        Assert.Equal(2, Assert.Single(summary.Addons!).Quantity);
        Assert.Equal("3", summary.Entitlements.Single(e => e.Key == EntitlementKeys.MaxUsers).Value);
    }
}
