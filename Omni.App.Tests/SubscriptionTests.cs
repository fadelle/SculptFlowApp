using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;

namespace Omni.App.Tests;

/// <summary>Subscriptions: start, renewal, credit reset, past due, expiry, cancellation, entitlements.</summary>
[Collection("Postgres")]
public class SubscriptionTests
{
    private readonly PostgresFixture _db;

    public SubscriptionTests(PostgresFixture db) => _db = db;

    private static readonly Dictionary<string, string> GrowthEntitlements = new()
    {
        [EntitlementKeys.Campaigns] = "true",
        [EntitlementKeys.AiAssistant] = "true",
        [EntitlementKeys.MaxUsers] = "5",
        [EntitlementKeys.MaxWhatsAppNumbers] = "1",
        [EntitlementKeys.MaxChannelConnections] = "2",
    };

    private static StartSubscriptionRequest Start(Guid client, string plan, bool charge = true) =>
        new(client, plan, Guid.NewGuid().ToString("N"), charge, BillingSource.Admin, "ops@omni", "test");

    [PostgresFact]
    public async Task CreateSubscription_ChargesThePrice_GrantsTheCredit_AndStartsAPeriod()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(price: 99m, credit: 25m, GrowthEntitlements);
        await h.TopUpAsync(client, 150m);

        var subscription = await h.Subscriptions.StartAsync(Start(client, plan));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(h.Time.Now, subscription.CurrentPeriodStart);
        Assert.Equal(h.Time.Now.AddMonths(1), subscription.CurrentPeriodEnd);
        var account = await h.AccountAsync(client);
        Assert.Equal(51m, account.WalletBalance);
        Assert.Equal(25m, account.IncludedCreditBalance);
        var ledger = await h.LedgerAsync(client);
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.SubscriptionCharge && l.Amount == -99m && l.PlanId != null && l.Actor == "ops@omni");
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.IncludedCreditGrant && l.Amount == 25m);
        await using (var db = h.Db())
        {
            Assert.True(await db.Events.AnyAsync(e => e.ClientId == client && e.EventType == BillingEventTypes.SubscriptionStarted));
        }
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task CreateSubscription_TheWalletCantPay_NothingChanges()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(price: 99m, credit: 25m);
        await h.TopUpAsync(client, 50m);

        await Assert.ThrowsAsync<BillingDeniedException>(() => h.Subscriptions.StartAsync(Start(client, plan)));

        Assert.Null(await h.Subscriptions.GetAsync(client));
        Assert.Equal(50m, (await h.AccountAsync(client)).WalletBalance);
        // A trial (first period not charged) works with any balance.
        var trial = await h.Subscriptions.StartAsync(Start(client, plan, charge: false));
        Assert.Equal(SubscriptionStatus.Active, trial.Status);
        Assert.Equal(25m, (await h.AccountAsync(client)).IncludedCreditBalance);
        Assert.Contains(await h.LedgerAsync(client), l => l.EntryType == LedgerEntryType.SubscriptionCharge && l.Amount == 0m);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task CreateSubscription_RetriedWithTheSameKey_ChargesOnce()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(price: 30m, credit: 5m);
        await h.TopUpAsync(client, 100m);
        var request = Start(client, plan);

        await h.Subscriptions.StartAsync(request);
        await h.Subscriptions.StartAsync(request);

        Assert.Equal(70m, (await h.AccountAsync(client)).WalletBalance);
        Assert.Single((await h.LedgerAsync(client)), l => l.EntryType == LedgerEntryType.SubscriptionCharge);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task Renewal_AdvancesThePeriod_ChargesAgain_AndResetsTheIncludedCredit()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var card = await h.CreateClientCardAsync(client);
        var evt = BillingHarness.Unique("evt");
        await h.AddRateAsync(card, evt, 4m);
        var plan = await h.CreatePlanAsync(price: 99m, credit: 25m, GrowthEntitlements);
        await h.TopUpAsync(client, 300m);
        var started = await h.Subscriptions.StartAsync(Start(client, plan));
        await h.Billing.ChargeAsync(h.Event(client, evt)); // spends $4 of the $25 credit

        h.Time.Now = started.CurrentPeriodEnd.AddMinutes(1);
        Assert.Equal(RenewalOutcome.Renewed, await h.Subscriptions.RenewIfDueAsync(client));
        Assert.Equal(RenewalOutcome.NotDue, await h.Subscriptions.RenewIfDueAsync(client)); // a second run does nothing

        var renewed = await h.Subscriptions.GetAsync(client);
        Assert.Equal(started.CurrentPeriodEnd, renewed!.CurrentPeriodStart);
        Assert.Equal(started.CurrentPeriodEnd.AddMonths(1), renewed.CurrentPeriodEnd);
        var account = await h.AccountAsync(client);
        Assert.Equal(25m, account.IncludedCreditBalance);   // reset, unused $21 did not roll over
        Assert.Equal(300m - 99m - 99m, account.WalletBalance);
        var ledger = await h.LedgerAsync(client);
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.IncludedCreditExpiry && l.Amount == -21m);
        Assert.Equal(2, ledger.Count(l => l.EntryType == LedgerEntryType.SubscriptionCharge));
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task Renewal_Unpaid_GoesPastDue_KeepsAccessDuringGrace_ThenExpires()
    {
        var h = new BillingHarness(_db.ConnectionString, graceDays: 7);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(price: 50m, credit: 10m, GrowthEntitlements);
        await h.TopUpAsync(client, 50m);
        var started = await h.Subscriptions.StartAsync(Start(client, plan)); // wallet now 0

        h.Time.Now = started.CurrentPeriodEnd.AddMinutes(1);
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(client));
        Assert.Equal(0m, (await h.AccountAsync(client)).IncludedCreditBalance); // the period's credit expired
        var pastDue = await h.Entitlements().GetAsync(client);
        Assert.Equal(SubscriptionStatus.PastDue, pastDue.SubscriptionStatus);
        Assert.True(pastDue.CanSendMessages);
        Assert.True(pastDue.CanUseCampaigns);

        h.Time.Advance(TimeSpan.FromDays(3));
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(client)); // still in grace, still unpaid

        h.Time.Advance(TimeSpan.FromDays(5));
        Assert.False((await h.Entitlements().GetAsync(client)).CanSendMessages); // grace is over even before the worker runs
        Assert.Equal(RenewalOutcome.Expired, await h.Subscriptions.RenewIfDueAsync(client));

        var expired = await h.Entitlements().GetAsync(client);
        Assert.Equal(SubscriptionStatus.Expired, expired.SubscriptionStatus);
        Assert.False(expired.CanSendMessages);
        Assert.False(expired.CanUseCampaigns);
        Assert.False(expired.CanUseAiAssistant);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => h.Entitlements().EnsureCanSendMessagesAsync(client));
        // History stays readable.
        Assert.NotEmpty((await h.Queries.GetSummaryAsync(client)).RecentTransactions);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task PastDue_ThenToppedUp_RenewsFromTheOldPeriodEnd()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(price: 20m, credit: 5m);
        await h.TopUpAsync(client, 20m);
        var started = await h.Subscriptions.StartAsync(Start(client, plan));

        h.Time.Now = started.CurrentPeriodEnd.AddHours(1);
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(client));
        h.Time.Advance(TimeSpan.FromDays(2));
        await h.TopUpAsync(client, 40m);
        await h.Subscriptions.ProcessDueAsync(); // what the maintenance worker runs

        var renewed = await h.Subscriptions.GetAsync(client);
        Assert.Equal(SubscriptionStatus.Active, renewed!.Status);
        Assert.Null(renewed.PastDueSince);
        Assert.Equal(started.CurrentPeriodEnd, renewed.CurrentPeriodStart);
        Assert.Equal(20m, (await h.AccountAsync(client)).WalletBalance);
        Assert.Equal(5m, (await h.AccountAsync(client)).IncludedCreditBalance);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task CancelAtPeriodEnd_EndsInsteadOfRenewing_AndCancelNowEndsImmediately()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var plan = await h.CreatePlanAsync(price: 10m, credit: 2m, GrowthEntitlements);
        await h.TopUpAsync(client, 100m);
        var started = await h.Subscriptions.StartAsync(Start(client, plan));

        await h.Subscriptions.CancelAsync(client, immediately: false, BillingSource.Admin, "ops", "client asked");
        Assert.True((await h.Entitlements().GetAsync(client)).CanUseCampaigns); // still paid for this period

        h.Time.Now = started.CurrentPeriodEnd.AddMinutes(1);
        Assert.Equal(RenewalOutcome.Cancelled, await h.Subscriptions.RenewIfDueAsync(client));
        Assert.Equal(90m, (await h.AccountAsync(client)).WalletBalance); // not charged again
        Assert.Equal(0m, (await h.AccountAsync(client)).IncludedCreditBalance);
        Assert.False((await h.Entitlements().GetAsync(client)).CanSendMessages);

        // Starting a plan again reactivates it.
        await h.Subscriptions.StartAsync(Start(client, plan));
        Assert.True((await h.Entitlements().GetAsync(client)).CanSendMessages);
        await h.Subscriptions.CancelAsync(client, immediately: true, BillingSource.Admin, "ops", "fraud");
        Assert.Equal(SubscriptionStatus.Cancelled, (await h.Subscriptions.GetAsync(client))!.Status);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task ChangingPlan_SwapsEntitlements_AndCredit()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var starter = await h.CreatePlanAsync(price: 0m, credit: 0m, new Dictionary<string, string> { [EntitlementKeys.Campaigns] = "false" });
        var growth = await h.CreatePlanAsync(price: 99m, credit: 25m, GrowthEntitlements);
        await h.TopUpAsync(client, 100m);

        await h.Subscriptions.StartAsync(Start(client, starter));
        Assert.False((await h.Entitlements().GetAsync(client)).CanUseCampaigns);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => h.Entitlements().EnsureFeatureAsync(client, EntitlementKeys.Campaigns));

        await h.Subscriptions.StartAsync(Start(client, growth));
        var e = await h.Entitlements().GetAsync(client);
        Assert.True(e.CanUseCampaigns);
        Assert.Equal(5, e.MaximumUsers);
        Assert.Equal(25m, (await h.AccountAsync(client)).IncludedCreditBalance);
        await using (var db = h.Db())
        {
            Assert.True(await db.Events.AnyAsync(x => x.ClientId == client && x.EventType == BillingEventTypes.SubscriptionPlanChanged));
        }
    }

    [PostgresFact]
    public async Task Entitlements_LimitsAndTheBillingSwitch()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();

        // No subscription at all: restricted (when billing is on) ...
        Assert.False((await h.Entitlements().GetAsync(client)).CanSendMessages);
        // ... but with billing off everything is allowed, as before this feature.
        var off = new BillingHarness(_db.ConnectionString, enabled: false);
        Assert.True((await off.Entitlements().GetAsync(client)).CanUseCampaigns);
        await off.Entitlements().EnsureCanConnectChannelAsync(client, ChannelType.WhatsApp);

        var plan = await h.CreatePlanAsync(price: 0m, credit: 0m, GrowthEntitlements);
        await h.Subscriptions.StartAsync(Start(client, plan));
        var entitlements = h.Entitlements();
        await entitlements.EnsureWithinLimitAsync(client, EntitlementKeys.MaxUsers, 5);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => entitlements.EnsureWithinLimitAsync(client, EntitlementKeys.MaxUsers, 6));

        // max_channel_connections = 2: WhatsApp + Telegram fit, a third (Facebook) doesn't; reconnecting doesn't double count.
        await using (var db = h.Db())
        {
            foreach (var channel in new[] { ChannelType.WhatsApp, ChannelType.Telegram })
            {
                db.ChannelIntegrations.Add(new ChannelIntegration
                {
                    Id = Guid.NewGuid(), ClientId = client, Channel = channel, Status = ChannelIntegrationStatus.Connected,
                    CreatedAt = h.Time.Now, UpdatedAt = h.Time.Now
                });
            }
            await db.SaveChangesAsync();
        }
        await h.Entitlements().EnsureCanConnectChannelAsync(client, ChannelType.WhatsApp);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => h.Entitlements().EnsureCanConnectChannelAsync(client, ChannelType.Facebook));
    }

    [PostgresFact]
    public async Task TheLedger_IsAppendOnly()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        await h.TopUpAsync(client, 10m);

        await using var db = h.Db();
        var entry = await db.BillingLedgerEntries.SingleAsync(l => l.ClientId == client);
        entry.Amount = 1000m;
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
