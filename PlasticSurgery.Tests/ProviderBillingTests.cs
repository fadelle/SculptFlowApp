using PlasticSurgery.Business.Services.Billing;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Tests;

/// <summary>Provider billing responsibility in the billing core: usage is recorded for every arrangement, but money
/// only moves for usage SculptFlow charges. (End-to-end WhatsApp sends per account are in MessageBillingTests.)</summary>
[Collection("Postgres")]
public class ProviderBillingTests
{
    private readonly PostgresFixture _db;

    public ProviderBillingTests(PostgresFixture db) => _db = db;

    private async Task<(BillingHarness H, Guid Clinic, string Card, string EventType)> SetupAsync(decimal wallet = 10m, decimal credit = 0m)
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        var evt = BillingHarness.Unique("evt");
        if (wallet > 0) await h.TopUpAsync(clinic, wallet);
        if (credit > 0)
        {
            await h.Billing.AdjustAsync(new WalletAdjustment(clinic, credit, LedgerBalanceType.IncludedCredit, "plan credit",
                Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));
        }
        return (h, clinic, card, evt);
    }

    [PostgresTheory]
    [InlineData(ProviderBillingResponsibility.CustomerDirect)]
    [InlineData(ProviderBillingResponsibility.ExternalProviderDirect)]
    [InlineData(ProviderBillingResponsibility.NoProviderUsageFee)]
    public async Task UsageSculptFlowDoesntCharge_NeedsNoRate_AndMovesNoMoney(string providerBilling)
    {
        var (h, clinic, _, evt) = await SetupAsync(wallet: 10m, credit: 25m); // no rate exists for evt at all

        var recorded = await h.Billing.ReserveAsync(h.Event(clinic, evt) with { ProviderBilling = providerBilling });
        var settled = await h.Billing.SettleAsync(clinic, h.Event(clinic, evt).IdempotencyKey); // unknown key: no-op

        Assert.Equal(UsageOutcome.Recorded, recorded.Outcome);
        Assert.True(recorded.Succeeded);
        Assert.Equal(UsageOutcome.NotFound, settled.Outcome);
        var usage = await h.UsageAsync(recorded.UsageRecordId!.Value);
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(providerBilling, usage.ProviderBilling);
        Assert.Null(usage.RateId);
        Assert.Null(usage.ProviderCost); // unknown: no rate gives it
        var account = await h.AccountAsync(clinic);
        Assert.Equal(10m, account.WalletBalance);
        Assert.Equal(25m, account.IncludedCreditBalance);
        Assert.Equal(0m, account.ReservedAmount);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task SculptFlowFundedUsage_StillRatesReservesAndUsesCreditThenWallet()
    {
        var (h, clinic, card, evt) = await SetupAsync(wallet: 10m, credit: 3m);
        await h.AddRateAsync(card, evt, clientRate: 5m, providerCost: 4m);

        var e = h.Event(clinic, evt) with { ProviderBilling = ProviderBillingResponsibility.PlatformFunded };
        Assert.Equal(UsageOutcome.Reserved, (await h.Billing.ReserveAsync(e)).Outcome);
        Assert.Equal(5m, (await h.AccountAsync(clinic)).ReservedAmount);
        var settled = await h.Billing.SettleAsync(clinic, e.IdempotencyKey);

        var usage = await h.UsageAsync(settled.UsageRecordId!.Value);
        Assert.Equal(3m, usage.CreditAmount);
        Assert.Equal(2m, usage.WalletAmount);
        Assert.Equal(ProviderOutcome.Billable, usage.ProviderOutcome);

        var released = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(released);
        await h.Billing.ReleaseAsync(clinic, released.IdempotencyKey, "delivery_failed");
        Assert.Equal(ProviderOutcome.NotBillable, (await h.UsageAsync((await h.Billing.ReleaseAsync(clinic, released.IdempotencyKey, "again")).UsageRecordId!.Value)).ProviderOutcome);
        Assert.Equal(8m, (await h.AccountAsync(clinic)).WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task SculptFlowCanAbsorbTheCost_OfAFundedAccount_WithoutChargingTheClinic()
    {
        var (h, clinic, card, evt) = await SetupAsync(wallet: 10m);
        await h.AddRateAsync(card, evt, clientRate: 0.07m, providerCost: 0.05m);

        var recorded = await h.Billing.ChargeAsync(h.Event(clinic, evt) with { ChargeUsage = false });

        var usage = await h.UsageAsync(recorded.UsageRecordId!.Value);
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, usage.ProviderBilling);
        Assert.Equal(0.05m, usage.ProviderCost); // SculptFlow's cost, recorded for margin reporting
        Assert.Equal(10m, (await h.AccountAsync(clinic)).WalletBalance);
    }

    [PostgresFact]
    public async Task UnchargedUsage_IsIdempotent_AndItsOutcomeIsRecordedOnce()
    {
        var (h, clinic, card, evt) = await SetupAsync();
        await h.AddRateAsync(card, evt, clientRate: 0.07m, providerCost: 0.05m);
        var e = h.Event(clinic, evt, quantity: 2) with { ProviderBilling = ProviderBillingResponsibility.CustomerDirect };

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => h.Billing.ReserveAsync(e)));
        Assert.Single(results.Select(r => r.UsageRecordId).Distinct());

        var first = await h.Billing.SettleAsync(clinic, e.IdempotencyKey, finalQuantity: 3);
        var again = await h.Billing.SettleAsync(clinic, e.IdempotencyKey);
        var lateFailure = await h.Billing.ReleaseAsync(clinic, e.IdempotencyKey, "late failure");

        Assert.Equal(UsageOutcome.Recorded, first.Outcome);
        Assert.True(again.Duplicate);
        Assert.Equal(UsageOutcome.Conflict, lateFailure.Outcome);
        var usage = await h.UsageAsync(first.UsageRecordId!.Value);
        Assert.Equal(3m, usage.Quantity);
        Assert.Equal(0.15m, usage.ProviderCost);
        Assert.Equal(ProviderOutcome.Billable, usage.ProviderOutcome);
        Assert.Equal(0m, usage.Amount);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task AccountDefaults_AndOverrides_ResolvePerConnectedAccount()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var whatsapp = await h.ConnectChannelAsync(clinic, ChannelType.WhatsApp, ChannelProvider.Meta);
        await h.ConnectChannelAsync(clinic, ChannelType.Telegram);

        var waDefault = await h.ProviderBilling.ResolveAsync(clinic, ChannelType.WhatsApp, ChannelProvider.Meta);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, waDefault.Responsibility); // unchanged behaviour
        Assert.True(waDefault.ChargesUsage);
        Assert.Equal(whatsapp, waDefault.ChannelIntegrationId);
        var telegram = await h.ProviderBilling.ResolveAsync(clinic, ChannelType.Telegram, null);
        Assert.Equal(ProviderBillingResponsibility.NoProviderUsageFee, telegram.Responsibility);
        Assert.False(telegram.ChargesUsage);

        await Assert.ThrowsAsync<ArgumentException>(() => h.ProviderBilling.SetAsync(whatsapp,
            new ChannelAccountBillingChange(ProviderBillingResponsibility.CustomerDirect, null, " ", "tests"))); // reason required
        await Assert.ThrowsAsync<ArgumentException>(() => h.ProviderBilling.SetAsync(whatsapp,
            new ChannelAccountBillingChange("meta_pays", null, "typo", "tests")));

        await h.ProviderBilling.SetAsync(whatsapp, new ChannelAccountBillingChange(ProviderBillingResponsibility.CustomerDirect, null,
            "Clinic's own WABA with its own Meta payment method", "ops@sculptflow"));
        var overridden = await h.ProviderBilling.ResolveAsync(clinic, ChannelType.WhatsApp, ChannelProvider.Meta);
        Assert.Equal(ProviderBillingResponsibility.CustomerDirect, overridden.Responsibility);
        Assert.False(overridden.ChargesUsage);
        Assert.Equal(ProviderBillingService.SourceAccount, overridden.Source);

        // The override was set for the Meta connection: if the account now goes through another provider, it no longer applies.
        var viaInfobip = await h.ProviderBilling.ResolveAsync(clinic, ChannelType.WhatsApp, ChannelProvider.Infobip);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, viaInfobip.Responsibility);

        var reset = await h.ProviderBilling.ResetAsync(whatsapp, "back to defaults", "ops@sculptflow");
        Assert.Equal(ProviderBillingService.SourceDefault, reset!.Source);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, reset.ProviderBilling);
    }

    [PostgresFact]
    public async Task TheReport_SeparatesCostPaidBySculptFlowFromCostPaidExternally()
    {
        var (h, clinic, card, evt) = await SetupAsync(wallet: 10m);
        await h.AddRateAsync(card, evt, clientRate: 0.065m, providerCost: 0.05m);

        await h.Billing.ChargeAsync(h.Event(clinic, evt) with { ProviderBilling = ProviderBillingResponsibility.PlatformFunded });
        await h.Billing.ChargeAsync(h.Event(clinic, evt) with { ProviderBilling = ProviderBillingResponsibility.CustomerDirect });

        var report = await h.Queries.GetReportAsync(h.Time.Now.AddDays(-1), h.Time.Now.AddDays(1));
        var funded = Assert.Single(report.Usage, r => r.ClinicId == clinic && r.ProviderBilling == ProviderBillingResponsibility.PlatformFunded);
        var direct = Assert.Single(report.Usage, r => r.ClinicId == clinic && r.ProviderBilling == ProviderBillingResponsibility.CustomerDirect);
        Assert.Equal(0.065m, funded.Revenue);
        Assert.Equal(0.05m, funded.ProviderCost);
        Assert.Equal(0.015m, funded.Margin);
        Assert.Equal(0m, direct.Revenue);
        Assert.Equal(0.05m, direct.ProviderCost);
        Assert.Equal(0m, direct.Margin); // SculptFlow neither paid nor charged
        Assert.True(report.ProviderCostPaidExternally >= 0.05m);
        Assert.Equal(9.935m, (await h.AccountAsync(clinic)).WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }
}
