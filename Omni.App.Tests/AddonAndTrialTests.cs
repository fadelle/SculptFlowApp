using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Services.Billing;
using Omni.App.Entities.Dtos.Notifications;
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

    /// <summary>The subscription service with a notification spy, to see what the client is told at a renewal.</summary>
    private static (SubscriptionService Service, Mock<INotificationService> Notifications) WithNotices(BillingHarness h)
    {
        var notifications = new Mock<INotificationService>();
        return (new SubscriptionService(h.Factory, h.Time, NullLogger<SubscriptionService>.Instance, h.Config, h.Cache, notifications.Object), notifications);
    }

    private static void VerifyNotice(Mock<INotificationService> notifications, Guid client, string titleKey, string addonName, Times times) =>
        notifications.Verify(n => n.CreateAsync(client, NotificationType.Billing,
            It.Is<NotificationText>(t => t.TitleKey == titleKey && t.Args.Count == 1 && ((string)t.Args[0]!).Contains(addonName)),
            null, null, null, null, "/settings/billing", It.IsAny<CancellationToken>()), times);

    private static async Task<List<JsonElement>> EventsAsync(BillingHarness h, Guid client, string type)
    {
        await using var db = h.Db();
        var metadata = await db.Events.Where(e => e.ClientId == client && e.EventType == type).Select(e => e.Metadata).ToListAsync();
        return metadata.Select(m => JsonDocument.Parse(m!).RootElement.Clone()).ToList();
    }

    /// <summary>Why add-ons ended at renewals ("not_renewed" / "insufficient_funds"), from their billing_addon_changed events.</summary>
    private static async Task<List<string>> AddonEndReasonsAsync(BillingHarness h, Guid client) =>
        (await EventsAsync(h, client, BillingEventTypes.AddonChanged))
            .Where(e => e.TryGetProperty("reason", out _)).Select(e => e.GetProperty("reason").GetString()!).ToList();

    [PostgresFact]
    public async Task At_renewal_an_add_on_nobody_set_to_renew_ends_uncharged_and_the_client_is_told()
    {
        // Decided 2026-11-05: add-ons end at renewal unless set to renew (Billing:AddonsRenewByDefault is off by default).
        var h = new BillingHarness(_db.ConnectionString);
        var (subscriptions, notices) = WithNotices(h);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(39m, 0m, new Dictionary<string, string> { [EntitlementKeys.MaxChannelConnections] = "2" });
        var addon = await AddonAsync(h, EntitlementKeys.MaxChannelConnections, 5m);
        await h.TopUpAsync(client, 39m + 10m + 39m + 10m);
        await h.Subscriptions.StartAsync(Start(client, plan));
        var rows = await Addons(h).SetQuantityAsync(client, addon, 2, "k-add", BillingSource.Admin, null);
        Assert.Equal(((bool?)null, false), (Assert.Single(rows).AutoRenew, rows[0].Renews));
        Assert.Equal(4, (await h.Entitlements().GetAsync(client)).MaximumChannelConnections);

        h.Time.Advance(TimeSpan.FromDays(32));
        Assert.Equal(RenewalOutcome.Renewed, await subscriptions.RenewIfDueAsync(client));

        Assert.Empty(await Addons(h).ListForClientAsync(client));
        Assert.Equal(10m, (await h.AccountAsync(client)).WalletBalance); // only the plan was charged
        Assert.Single(await h.LedgerAsync(client), l => l.EntryType == LedgerEntryType.AddonCharge); // the first purchase only
        Assert.Equal(2, (await h.Entitlements().GetAsync(client)).MaximumChannelConnections); // the cache was cleared
        Assert.Equal(["not_renewed"], await AddonEndReasonsAsync(h, client));
        VerifyNotice(notices, client, "notifications.billing.addonsEnded.title", "Extra " + EntitlementKeys.MaxChannelConnections, Times.Once());
        VerifyNotice(notices, client, "notifications.billing.addonsUnpaid.title", "", Times.Never());
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task A_renewing_add_on_is_charged_with_the_plan_and_one_the_wallet_cant_pay_ends_while_the_plan_renews()
    {
        // Decided 2026-11-05: a short wallet renews the plan and drops the add-ons it can't pay, never the plan.
        var h = new BillingHarness(_db.ConnectionString);
        var (subscriptions, notices) = WithNotices(h);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(39m, 0m, new Dictionary<string, string>
        {
            [EntitlementKeys.MaxUsers] = "2", [EntitlementKeys.MaxAiBots] = "1",
        });
        var seats = await AddonAsync(h, EntitlementKeys.MaxUsers, 5m);
        var bots = await AddonAsync(h, EntitlementKeys.MaxAiBots, 15m);
        await h.TopUpAsync(client, 39m + 10m + 15m + 39m + 10m); // the next period pays the plan and the seats, not the bot
        await h.Subscriptions.StartAsync(Start(client, plan));
        await Addons(h).SetQuantityAsync(client, seats, 2, "k-seats", BillingSource.Admin, null);
        await Addons(h).SetQuantityAsync(client, bots, 1, "k-bots", BillingSource.Admin, null);
        await Addons(h).SetAutoRenewAsync(client, seats, true, BillingSource.Client, "owner");
        var rows = await Addons(h).SetAutoRenewAsync(client, bots, true, BillingSource.Admin, "ops@omni");
        Assert.All(rows, r => Assert.Equal(((bool?)true, true), (r.AutoRenew, r.Renews)));

        h.Time.Advance(TimeSpan.FromDays(32));
        Assert.Equal(RenewalOutcome.Renewed, await subscriptions.RenewIfDueAsync(client));

        var kept = Assert.Single(await Addons(h).ListForClientAsync(client));
        Assert.Equal((seats, 2, (bool?)true), (kept.Code, kept.Quantity, kept.AutoRenew));
        Assert.Equal(0m, (await h.AccountAsync(client)).WalletBalance);
        var addonCharges = (await h.LedgerAsync(client)).Where(l => l.EntryType == LedgerEntryType.AddonCharge).Select(l => l.Amount).Order().ToList();
        Assert.Equal(new[] { -15m, -10m, -10m }, addonCharges); // bought the bot, bought the seats, renewed the seats
        var entitlements = await h.Entitlements().GetAsync(client);
        Assert.Equal((4, 1), (entitlements.MaximumUsers, entitlements.MaximumAiBots));
        Assert.Equal(["insufficient_funds"], await AddonEndReasonsAsync(h, client));
        VerifyNotice(notices, client, "notifications.billing.addonsUnpaid.title", "Extra " + EntitlementKeys.MaxAiBots, Times.Once());
        VerifyNotice(notices, client, "notifications.billing.addonsEnded.title", "", Times.Never());
        var renewed = Assert.Single(await EventsAsync(h, client, BillingEventTypes.SubscriptionRenewed));
        Assert.Equal(10m, renewed.GetProperty("addons").GetDecimal());
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task The_renew_by_default_setting_renews_add_ons_nobody_chose_for_and_a_no_still_ends()
    {
        var h = new BillingHarness(_db.ConnectionString);
        h.SetSetting("Billing", "AddonsRenewByDefault", "true");
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(20m, 0m, new Dictionary<string, string>
        {
            [EntitlementKeys.MaxWhatsAppNumbers] = "1", [EntitlementKeys.MaxChannelConnections] = "1",
        });
        var numbers = await AddonAsync(h, EntitlementKeys.MaxWhatsAppNumbers, 4m);
        var channels = await AddonAsync(h, EntitlementKeys.MaxChannelConnections, 3m);
        await h.TopUpAsync(client, 100m);
        await h.Subscriptions.StartAsync(Start(client, plan));
        await Addons(h).SetQuantityAsync(client, numbers, 1, "k-n", BillingSource.Admin, null);
        await Addons(h).SetQuantityAsync(client, channels, 1, "k-c", BillingSource.Admin, null);
        var rows = await Addons(h).SetAutoRenewAsync(client, channels, false, BillingSource.Client, "owner");
        var numbersRow = rows.Single(r => r.Code == numbers);
        Assert.Equal(((bool?)null, true), (numbersRow.AutoRenew, numbersRow.Renews));
        Assert.False(rows.Single(r => r.Code == channels).Renews);

        h.Time.Advance(TimeSpan.FromDays(32));
        Assert.Equal(RenewalOutcome.Renewed, await h.Subscriptions.RenewIfDueAsync(client));

        Assert.Equal(numbers, Assert.Single(await Addons(h).ListForClientAsync(client)).Code);
        Assert.Equal(100m - 20m - 4m - 3m - 20m - 4m, (await h.AccountAsync(client)).WalletBalance);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task A_plan_the_wallet_cant_pay_goes_past_due_ends_the_non_renewing_add_ons_and_keeps_the_renewing_ones()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var (subscriptions, notices) = WithNotices(h);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(39m, 0m, new Dictionary<string, string>
        {
            [EntitlementKeys.MaxUsers] = "2", [EntitlementKeys.MaxAiBots] = "1",
        });
        var seats = await AddonAsync(h, EntitlementKeys.MaxUsers, 5m);
        var bots = await AddonAsync(h, EntitlementKeys.MaxAiBots, 15m);
        await h.TopUpAsync(client, 39m + 5m + 15m + 20m); // 20 left: not enough for the plan
        await h.Subscriptions.StartAsync(Start(client, plan));
        await Addons(h).SetQuantityAsync(client, seats, 1, "k-seats", BillingSource.Admin, null);
        await Addons(h).SetQuantityAsync(client, bots, 1, "k-bots", BillingSource.Admin, null);
        await Addons(h).SetAutoRenewAsync(client, bots, true, BillingSource.Client, "owner");

        h.Time.Advance(TimeSpan.FromDays(32));
        Assert.Equal(RenewalOutcome.PastDue, await subscriptions.RenewIfDueAsync(client));
        Assert.Equal(bots, Assert.Single(await Addons(h).ListForClientAsync(client)).Code);
        Assert.Equal(20m, (await h.AccountAsync(client)).WalletBalance);
        var pastDue = Assert.Single(await EventsAsync(h, client, BillingEventTypes.SubscriptionPastDue));
        Assert.Equal(39m, pastDue.GetProperty("due").GetDecimal()); // the plan alone is what keeps the client going

        // Still in grace and still short: nothing more ends and nobody is told twice.
        h.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(RenewalOutcome.PastDue, await subscriptions.RenewIfDueAsync(client));
        VerifyNotice(notices, client, "notifications.billing.addonsEnded.title", "Extra " + EntitlementKeys.MaxUsers, Times.Once());

        // Paid in grace: the plan renews and the renewing bot is charged with it.
        await h.TopUpAsync(client, 34m);
        Assert.Equal(RenewalOutcome.Renewed, await subscriptions.RenewIfDueAsync(client));
        Assert.Equal(bots, Assert.Single(await Addons(h).ListForClientAsync(client)).Code);
        Assert.Equal(0m, (await h.AccountAsync(client)).WalletBalance);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task A_renewal_choice_for_an_add_on_the_client_doesnt_have_is_not_found_and_an_unchanged_one_logs_nothing()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(10m, 0m, new Dictionary<string, string> { [EntitlementKeys.MaxUsers] = "1" });
        var seats = await AddonAsync(h, EntitlementKeys.MaxUsers, 1m);
        await h.TopUpAsync(client, 20m);
        await h.Subscriptions.StartAsync(Start(client, plan));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Addons(h).SetAutoRenewAsync(client, seats, true, BillingSource.Client, "owner"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Addons(h).SetAutoRenewAsync(client, "no-such-addon", true, BillingSource.Client, "owner"));

        await Addons(h).SetQuantityAsync(client, seats, 1, "k", BillingSource.Admin, null);
        await Addons(h).SetAutoRenewAsync(client, seats, true, BillingSource.Client, "owner");
        await Addons(h).SetAutoRenewAsync(client, seats, true, BillingSource.Client, "owner"); // unchanged: no event
        var back = await Addons(h).SetAutoRenewAsync(client, " " + seats.ToUpperInvariant() + " ", null, BillingSource.Admin, "ops@omni");

        Assert.Equal(((bool?)null, false), (Assert.Single(back).AutoRenew, back[0].Renews));
        Assert.Equal(3, (await EventsAsync(h, client, BillingEventTypes.AddonChanged)).Count); // bought, yes, back to default
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
