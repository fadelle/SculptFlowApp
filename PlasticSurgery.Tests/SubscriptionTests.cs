using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Billing;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Tests;

/// <summary>Subscriptions: start, renewal, credit reset, past due, expiry, cancellation, entitlements.</summary>
[Collection("Postgres")]
public class SubscriptionTests
{
    private readonly PostgresFixture _db;

    public SubscriptionTests(PostgresFixture db) => _db = db;

    private static readonly Dictionary<string, string> GrowthEntitlements = new()
    {
        [EntitlementKeys.Campaigns] = "true",
        [EntitlementKeys.AiAgent] = "true",
        [EntitlementKeys.MaxAgents] = "5",
        [EntitlementKeys.MaxWhatsAppNumbers] = "1",
        [EntitlementKeys.MaxChannelConnections] = "2",
    };

    private static StartSubscriptionRequest Start(Guid clinic, string plan, bool charge = true) =>
        new(clinic, plan, Guid.NewGuid().ToString("N"), charge, BillingSource.Admin, "ops@sculptflow", "test");

    [PostgresFact]
    public async Task CreateSubscription_ChargesThePrice_GrantsTheCredit_AndStartsAPeriod()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var plan = await h.CreatePlanAsync(price: 99m, credit: 25m, GrowthEntitlements);
        await h.TopUpAsync(clinic, 150m);

        var subscription = await h.Subscriptions.StartAsync(Start(clinic, plan));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(h.Time.Now, subscription.CurrentPeriodStart);
        Assert.Equal(h.Time.Now.AddMonths(1), subscription.CurrentPeriodEnd);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(51m, account.WalletBalance);
        Assert.Equal(25m, account.IncludedCreditBalance);
        var ledger = await h.LedgerAsync(clinic);
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.SubscriptionCharge && l.Amount == -99m && l.PlanId != null && l.Actor == "ops@sculptflow");
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.IncludedCreditGrant && l.Amount == 25m);
        await using (var db = h.Db())
        {
            Assert.True(await db.Events.AnyAsync(e => e.ClinicId == clinic && e.EventType == BillingEventTypes.SubscriptionStarted));
        }
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task CreateSubscription_TheWalletCantPay_NothingChanges()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var plan = await h.CreatePlanAsync(price: 99m, credit: 25m);
        await h.TopUpAsync(clinic, 50m);

        await Assert.ThrowsAsync<BillingDeniedException>(() => h.Subscriptions.StartAsync(Start(clinic, plan)));

        Assert.Null(await h.Subscriptions.GetAsync(clinic));
        Assert.Equal(50m, (await h.AccountAsync(clinic)).WalletBalance);
        // A trial (first period not charged) works with any balance.
        var trial = await h.Subscriptions.StartAsync(Start(clinic, plan, charge: false));
        Assert.Equal(SubscriptionStatus.Active, trial.Status);
        Assert.Equal(25m, (await h.AccountAsync(clinic)).IncludedCreditBalance);
        Assert.Contains(await h.LedgerAsync(clinic), l => l.EntryType == LedgerEntryType.SubscriptionCharge && l.Amount == 0m);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task CreateSubscription_RetriedWithTheSameKey_ChargesOnce()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var plan = await h.CreatePlanAsync(price: 30m, credit: 5m);
        await h.TopUpAsync(clinic, 100m);
        var request = Start(clinic, plan);

        await h.Subscriptions.StartAsync(request);
        await h.Subscriptions.StartAsync(request);

        Assert.Equal(70m, (await h.AccountAsync(clinic)).WalletBalance);
        Assert.Single((await h.LedgerAsync(clinic)), l => l.EntryType == LedgerEntryType.SubscriptionCharge);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Renewal_AdvancesThePeriod_ChargesAgain_AndResetsTheIncludedCredit()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        var evt = BillingHarness.Unique("evt");
        await h.AddRateAsync(card, evt, 4m);
        var plan = await h.CreatePlanAsync(price: 99m, credit: 25m, GrowthEntitlements);
        await h.TopUpAsync(clinic, 300m);
        var started = await h.Subscriptions.StartAsync(Start(clinic, plan));
        await h.Billing.ChargeAsync(h.Event(clinic, evt)); // spends $4 of the $25 credit

        h.Time.Now = started.CurrentPeriodEnd.AddMinutes(1);
        Assert.Equal(RenewalOutcome.Renewed, await h.Subscriptions.RenewIfDueAsync(clinic));
        Assert.Equal(RenewalOutcome.NotDue, await h.Subscriptions.RenewIfDueAsync(clinic)); // a second run does nothing

        var renewed = await h.Subscriptions.GetAsync(clinic);
        Assert.Equal(started.CurrentPeriodEnd, renewed!.CurrentPeriodStart);
        Assert.Equal(started.CurrentPeriodEnd.AddMonths(1), renewed.CurrentPeriodEnd);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(25m, account.IncludedCreditBalance);   // reset, unused $21 did not roll over
        Assert.Equal(300m - 99m - 99m, account.WalletBalance);
        var ledger = await h.LedgerAsync(clinic);
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.IncludedCreditExpiry && l.Amount == -21m);
        Assert.Equal(2, ledger.Count(l => l.EntryType == LedgerEntryType.SubscriptionCharge));
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Renewal_Unpaid_GoesPastDue_KeepsAccessDuringGrace_ThenExpires()
    {
        var h = new BillingHarness(_db.ConnectionString, graceDays: 7);
        var clinic = await h.CreateClinicAsync();
        var plan = await h.CreatePlanAsync(price: 50m, credit: 10m, GrowthEntitlements);
        await h.TopUpAsync(clinic, 50m);
        var started = await h.Subscriptions.StartAsync(Start(clinic, plan)); // wallet now 0

        h.Time.Now = started.CurrentPeriodEnd.AddMinutes(1);
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(clinic));
        Assert.Equal(0m, (await h.AccountAsync(clinic)).IncludedCreditBalance); // the period's credit expired
        var pastDue = await h.Entitlements().GetAsync(clinic);
        Assert.Equal(SubscriptionStatus.PastDue, pastDue.SubscriptionStatus);
        Assert.True(pastDue.CanSendMessages);
        Assert.True(pastDue.CanUseCampaigns);

        h.Time.Advance(TimeSpan.FromDays(3));
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(clinic)); // still in grace, still unpaid

        h.Time.Advance(TimeSpan.FromDays(5));
        Assert.False((await h.Entitlements().GetAsync(clinic)).CanSendMessages); // grace is over even before the worker runs
        Assert.Equal(RenewalOutcome.Expired, await h.Subscriptions.RenewIfDueAsync(clinic));

        var expired = await h.Entitlements().GetAsync(clinic);
        Assert.Equal(SubscriptionStatus.Expired, expired.SubscriptionStatus);
        Assert.False(expired.CanSendMessages);
        Assert.False(expired.CanUseCampaigns);
        Assert.False(expired.CanUseAiAgent);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => h.Entitlements().EnsureCanSendMessagesAsync(clinic));
        // History stays readable.
        Assert.NotEmpty((await h.Queries.GetSummaryAsync(clinic)).RecentTransactions);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task PastDue_ThenToppedUp_RenewsFromTheOldPeriodEnd()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var plan = await h.CreatePlanAsync(price: 20m, credit: 5m);
        await h.TopUpAsync(clinic, 20m);
        var started = await h.Subscriptions.StartAsync(Start(clinic, plan));

        h.Time.Now = started.CurrentPeriodEnd.AddHours(1);
        Assert.Equal(RenewalOutcome.PastDue, await h.Subscriptions.RenewIfDueAsync(clinic));
        h.Time.Advance(TimeSpan.FromDays(2));
        await h.TopUpAsync(clinic, 40m);
        await h.Subscriptions.ProcessDueAsync(); // what the maintenance worker runs

        var renewed = await h.Subscriptions.GetAsync(clinic);
        Assert.Equal(SubscriptionStatus.Active, renewed!.Status);
        Assert.Null(renewed.PastDueSince);
        Assert.Equal(started.CurrentPeriodEnd, renewed.CurrentPeriodStart);
        Assert.Equal(20m, (await h.AccountAsync(clinic)).WalletBalance);
        Assert.Equal(5m, (await h.AccountAsync(clinic)).IncludedCreditBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task CancelAtPeriodEnd_EndsInsteadOfRenewing_AndCancelNowEndsImmediately()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var plan = await h.CreatePlanAsync(price: 10m, credit: 2m, GrowthEntitlements);
        await h.TopUpAsync(clinic, 100m);
        var started = await h.Subscriptions.StartAsync(Start(clinic, plan));

        await h.Subscriptions.CancelAsync(clinic, immediately: false, BillingSource.Admin, "ops", "client asked");
        Assert.True((await h.Entitlements().GetAsync(clinic)).CanUseCampaigns); // still paid for this period

        h.Time.Now = started.CurrentPeriodEnd.AddMinutes(1);
        Assert.Equal(RenewalOutcome.Cancelled, await h.Subscriptions.RenewIfDueAsync(clinic));
        Assert.Equal(90m, (await h.AccountAsync(clinic)).WalletBalance); // not charged again
        Assert.Equal(0m, (await h.AccountAsync(clinic)).IncludedCreditBalance);
        Assert.False((await h.Entitlements().GetAsync(clinic)).CanSendMessages);

        // Starting a plan again reactivates it.
        await h.Subscriptions.StartAsync(Start(clinic, plan));
        Assert.True((await h.Entitlements().GetAsync(clinic)).CanSendMessages);
        await h.Subscriptions.CancelAsync(clinic, immediately: true, BillingSource.Admin, "ops", "fraud");
        Assert.Equal(SubscriptionStatus.Cancelled, (await h.Subscriptions.GetAsync(clinic))!.Status);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task ChangingPlan_SwapsEntitlements_AndCredit()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var starter = await h.CreatePlanAsync(price: 0m, credit: 0m, new Dictionary<string, string> { [EntitlementKeys.Campaigns] = "false" });
        var growth = await h.CreatePlanAsync(price: 99m, credit: 25m, GrowthEntitlements);
        await h.TopUpAsync(clinic, 100m);

        await h.Subscriptions.StartAsync(Start(clinic, starter));
        Assert.False((await h.Entitlements().GetAsync(clinic)).CanUseCampaigns);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => h.Entitlements().EnsureFeatureAsync(clinic, EntitlementKeys.Campaigns));

        await h.Subscriptions.StartAsync(Start(clinic, growth));
        var e = await h.Entitlements().GetAsync(clinic);
        Assert.True(e.CanUseCampaigns);
        Assert.Equal(5, e.MaximumAgents);
        Assert.Equal(25m, (await h.AccountAsync(clinic)).IncludedCreditBalance);
        await using (var db = h.Db())
        {
            Assert.True(await db.Events.AnyAsync(x => x.ClinicId == clinic && x.EventType == BillingEventTypes.SubscriptionPlanChanged));
        }
    }

    [PostgresFact]
    public async Task Entitlements_LimitsAndTheBillingSwitch()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();

        // No subscription at all: restricted (when billing is on) ...
        Assert.False((await h.Entitlements().GetAsync(clinic)).CanSendMessages);
        // ... but with billing off everything is allowed, as before this feature.
        var off = new BillingHarness(_db.ConnectionString, enabled: false);
        Assert.True((await off.Entitlements().GetAsync(clinic)).CanUseCampaigns);
        await off.Entitlements().EnsureCanConnectChannelAsync(clinic, ChannelType.WhatsApp);

        var plan = await h.CreatePlanAsync(price: 0m, credit: 0m, GrowthEntitlements);
        await h.Subscriptions.StartAsync(Start(clinic, plan));
        var entitlements = h.Entitlements();
        await entitlements.EnsureWithinLimitAsync(clinic, EntitlementKeys.MaxAgents, 5);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => entitlements.EnsureWithinLimitAsync(clinic, EntitlementKeys.MaxAgents, 6));

        // max_channel_connections = 2: WhatsApp + Telegram fit, a third (Facebook) doesn't; reconnecting doesn't double count.
        await using (var db = h.Db())
        {
            foreach (var channel in new[] { ChannelType.WhatsApp, ChannelType.Telegram })
            {
                db.ChannelIntegrations.Add(new ChannelIntegration
                {
                    Id = Guid.NewGuid(), ClinicId = clinic, Channel = channel, Status = ChannelIntegrationStatus.Connected,
                    CreatedAt = h.Time.Now, UpdatedAt = h.Time.Now
                });
            }
            await db.SaveChangesAsync();
        }
        await h.Entitlements().EnsureCanConnectChannelAsync(clinic, ChannelType.WhatsApp);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => h.Entitlements().EnsureCanConnectChannelAsync(clinic, ChannelType.Facebook));
    }

    [PostgresFact]
    public async Task TheLedger_IsAppendOnly()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        await h.TopUpAsync(clinic, 10m);

        await using var db = h.Db();
        var entry = await db.BillingLedgerEntries.SingleAsync(l => l.ClinicId == clinic);
        entry.Amount = 1000m;
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
