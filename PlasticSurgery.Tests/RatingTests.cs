using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Billing;

namespace PlasticSurgery.Tests;

/// <summary>Rate cards against the database: hierarchy, specificity, versioning, missing rates, immutability.</summary>
[Collection("Postgres")]
public class RatingTests
{
    private readonly PostgresFixture _db;

    public RatingTests(PostgresFixture db) => _db = db;

    /// <summary>The global default card (one per database), created once.</summary>
    private static async Task<string> DefaultCardAsync(BillingHarness h)
    {
        var existing = (await h.RateCards.ListAsync()).FirstOrDefault(c => c.IsDefault);
        if (existing is not null) return existing.Code;
        return (await h.RateCards.CreateAsync(new RateCardRequest("default", "Default prices", null, null, IsDefault: true))).Code;
    }

    [PostgresFact]
    public async Task ARateExists_TheUsageIsPricedWithIt()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var evt = BillingHarness.Unique("evt");
        await h.AddRateAsync(await DefaultCardAsync(h), evt, 0.0123m, 0.01m);
        await h.TopUpAsync(clinic, 1m);

        var result = await h.Billing.ReserveAsync(h.Event(clinic, evt));

        Assert.Equal(UsageOutcome.Reserved, result.Outcome);
        Assert.Equal(0.0123m, result.Amount);
        Assert.Equal(RateSource.Default, (await h.UsageAsync(result.UsageRecordId!.Value)).RateSource);
    }

    [PostgresFact]
    public async Task AMissingRate_IsRefused_NeverGuessed()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        await h.TopUpAsync(clinic, 100m);

        var result = await h.Billing.ReserveAsync(h.Event(clinic, BillingHarness.Unique("unpriced")));

        Assert.Equal(UsageOutcome.Failed, result.Outcome);
        Assert.Equal(UsageFailureReason.RateNotFound, result.FailureReason);
        var usage = await h.UsageAsync(result.UsageRecordId!.Value);
        Assert.Null(usage.Amount);
        Assert.Null(usage.RateId);
        Assert.Equal(0m, (await h.AccountAsync(clinic)).ReservedAmount);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task ClientRates_OverrideThePlanCard_WhichOverridesTheDefault()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var evt = BillingHarness.Unique("evt");
        var defaultCard = await DefaultCardAsync(h);
        await h.AddRateAsync(defaultCard, evt, 1.00m);
        var planCard = (await h.RateCards.CreateAsync(new RateCardRequest(BillingHarness.Unique("plancard").Replace('_', '-'), "Growth prices", null, null))).Code;
        await h.AddRateAsync(planCard, evt, 0.80m);
        var plan = await h.CreatePlanAsync(0m, 0m, rateCardCode: planCard);

        var onDefault = await h.CreateClinicAsync();
        var onPlan = await h.CreateClinicAsync();
        var custom = await h.CreateClinicAsync();
        foreach (var c in new[] { onDefault, onPlan, custom }) await h.TopUpAsync(c, 10m);
        await h.Subscriptions.StartAsync(new StartSubscriptionRequest(onPlan, plan, Guid.NewGuid().ToString("N"), true, BillingSource.Admin));
        await h.Subscriptions.StartAsync(new StartSubscriptionRequest(custom, plan, Guid.NewGuid().ToString("N"), true, BillingSource.Admin));
        var customCard = await h.CreateClientCardAsync(custom);
        await h.AddRateAsync(customCard, evt, 0.50m);

        Assert.Equal(1.00m, (await h.Billing.ReserveAsync(h.Event(onDefault, evt))).Amount);
        Assert.Equal(0.80m, (await h.Billing.ReserveAsync(h.Event(onPlan, evt))).Amount);
        Assert.Equal(0.50m, (await h.Billing.ReserveAsync(h.Event(custom, evt))).Amount);

        var quote = await h.Queries.QuoteAsync(custom, evt, null, null, null, null);
        Assert.Equal(RateSource.Client, quote!.RateSource);
    }

    [PostgresFact]
    public async Task DestinationAndProvider_SelectTheMostSpecificRate()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        var evt = BillingHarness.Unique("wa_marketing");
        await h.AddRateAsync(card, evt, 0.10m);                                   // anywhere
        await h.AddRateAsync(card, evt, 0.07m, country: "LB");                    // Lebanon
        await h.AddRateAsync(card, evt, 0.06m, country: "LB", provider: "infobip"); // Lebanon through Infobip
        await h.TopUpAsync(clinic, 10m);

        Assert.Equal(0.10m, (await h.Billing.ReserveAsync(h.Event(clinic, evt, country: "FR"))).Amount);
        Assert.Equal(0.07m, (await h.Billing.ReserveAsync(h.Event(clinic, evt, country: "LB", provider: "meta"))).Amount);
        Assert.Equal(0.06m, (await h.Billing.ReserveAsync(h.Event(clinic, evt, country: "lb", provider: "INFOBIP"))).Amount);
    }

    [PostgresFact]
    public async Task APriceChange_NeverChangesHistoricalUsage_AndUsageIsPricedAtItsOwnTime()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        var evt = BillingHarness.Unique("evt");
        await h.TopUpAsync(clinic, 10m);
        var v1Start = h.Time.Now;
        await h.AddRateAsync(card, evt, 0.05m);
        var before = await h.Billing.ChargeAsync(h.Event(clinic, evt));

        h.Time.Advance(TimeSpan.FromDays(10));
        var v2 = await h.AddRateAsync(card, evt, 0.09m); // new version from "now", closes v1
        var after = await h.Billing.ChargeAsync(h.Event(clinic, evt));
        var lateCallbackForOldUsage = await h.Billing.ChargeAsync(h.Event(clinic, evt, occurredAt: v1Start.AddDays(1)));

        Assert.Equal(0.05m, (await h.UsageAsync(before.UsageRecordId!.Value)).Amount);
        Assert.Equal(0.09m, (await h.UsageAsync(after.UsageRecordId!.Value)).Amount);
        Assert.Equal(0.05m, lateCallbackForOldUsage.Amount);

        var versions = await h.RateCards.ListRatesAsync(card, includeHistory: true);
        Assert.Equal(2, versions!.Count);
        Assert.Equal(v2!.EffectiveFrom, versions.Single(v => v.ClientRate == 0.05m).EffectiveTo);
        Assert.Single((await h.RateCards.ListRatesAsync(card, includeHistory: false))!);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Rates_CantStartInThePast_OrOverlap_OrBeEdited()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        var evt = BillingHarness.Unique("evt");

        await Assert.ThrowsAsync<ArgumentException>(() => h.AddRateAsync(card, evt, 1m, from: h.Time.Now.AddDays(-1)));

        var rate = await h.AddRateAsync(card, evt, 1m);
        await Assert.ThrowsAsync<ArgumentException>(() => h.AddRateAsync(card, evt, 2m, from: rate!.EffectiveFrom));

        Guid cardId;
        // The database itself refuses to re-price a version (immutability trigger) ...
        await using (var db = h.Db())
        {
            var row = await db.BillingRates.SingleAsync(r => r.Id == rate!.Id);
            cardId = row.RateCardId;
            row.ClientRate = 5m;
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // ... and two overlapping versions of the same rate (exclusion constraint), even written around the service.
        await using (var db = h.Db())
        {
            db.BillingRates.Add(new BillingRate
            {
                Id = Guid.NewGuid(), RateCardId = cardId, EventType = evt, Unit = "message", ClientRate = 3m, Currency = "USD",
                EffectiveFrom = h.Time.Now.AddHours(1), CreatedAt = h.Time.Now
            });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
