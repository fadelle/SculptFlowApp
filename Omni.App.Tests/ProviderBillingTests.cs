using Omni.App.Business.Services.Billing;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;

namespace Omni.App.Tests;

/// <summary>Provider billing responsibility in the billing core: usage is recorded for every arrangement, but money
/// only moves for usage Omni charges. (End-to-end WhatsApp sends per account are in MessageBillingTests.)</summary>
[Collection("Postgres")]
public class ProviderBillingTests
{
    private readonly PostgresFixture _db;

    public ProviderBillingTests(PostgresFixture db) => _db = db;

    private async Task<(BillingHarness H, Guid Client, string Card, string EventType)> SetupAsync(decimal wallet = 10m, decimal credit = 0m)
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var card = await h.CreateClientCardAsync(client);
        var evt = BillingHarness.Unique("evt");
        if (wallet > 0) await h.TopUpAsync(client, wallet);
        if (credit > 0)
        {
            await h.Billing.AdjustAsync(new WalletAdjustment(client, credit, LedgerBalanceType.IncludedCredit, "plan credit",
                Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));
        }
        return (h, client, card, evt);
    }

    [PostgresTheory]
    [InlineData(ProviderBillingResponsibility.CustomerDirect)]
    [InlineData(ProviderBillingResponsibility.ExternalProviderDirect)]
    [InlineData(ProviderBillingResponsibility.NoProviderUsageFee)]
    public async Task UsagePlatformDoesntCharge_NeedsNoRate_AndMovesNoMoney(string providerBilling)
    {
        var (h, client, _, evt) = await SetupAsync(wallet: 10m, credit: 25m); // no rate exists for evt at all

        var recorded = await h.Billing.ReserveAsync(h.Event(client, evt) with { ProviderBilling = providerBilling });
        var settled = await h.Billing.SettleAsync(client, h.Event(client, evt).IdempotencyKey); // unknown key: no-op

        Assert.Equal(UsageOutcome.Recorded, recorded.Outcome);
        Assert.True(recorded.Succeeded);
        Assert.Equal(UsageOutcome.NotFound, settled.Outcome);
        var usage = await h.UsageAsync(recorded.UsageRecordId!.Value);
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(providerBilling, usage.ProviderBilling);
        Assert.Null(usage.RateId);
        Assert.Null(usage.ProviderCost); // unknown: no rate gives it
        var account = await h.AccountAsync(client);
        Assert.Equal(10m, account.WalletBalance);
        Assert.Equal(25m, account.IncludedCreditBalance);
        Assert.Equal(0m, account.ReservedAmount);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task PlatformFundedUsage_StillRatesReservesAndUsesCreditThenWallet()
    {
        var (h, client, card, evt) = await SetupAsync(wallet: 10m, credit: 3m);
        await h.AddRateAsync(card, evt, clientRate: 5m, providerCost: 4m);

        var e = h.Event(client, evt) with { ProviderBilling = ProviderBillingResponsibility.PlatformFunded };
        Assert.Equal(UsageOutcome.Reserved, (await h.Billing.ReserveAsync(e)).Outcome);
        Assert.Equal(5m, (await h.AccountAsync(client)).ReservedAmount);
        var settled = await h.Billing.SettleAsync(client, e.IdempotencyKey);

        var usage = await h.UsageAsync(settled.UsageRecordId!.Value);
        Assert.Equal(3m, usage.CreditAmount);
        Assert.Equal(2m, usage.WalletAmount);
        Assert.Equal(ProviderOutcome.Billable, usage.ProviderOutcome);

        var released = h.Event(client, evt);
        await h.Billing.ReserveAsync(released);
        await h.Billing.ReleaseAsync(client, released.IdempotencyKey, "delivery_failed");
        Assert.Equal(ProviderOutcome.NotBillable, (await h.UsageAsync((await h.Billing.ReleaseAsync(client, released.IdempotencyKey, "again")).UsageRecordId!.Value)).ProviderOutcome);
        Assert.Equal(8m, (await h.AccountAsync(client)).WalletBalance);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task PlatformCanAbsorbTheCost_OfAFundedAccount_WithoutChargingTheClient()
    {
        var (h, client, card, evt) = await SetupAsync(wallet: 10m);
        await h.AddRateAsync(card, evt, clientRate: 0.07m, providerCost: 0.05m);

        var recorded = await h.Billing.ChargeAsync(h.Event(client, evt) with { ChargeUsage = false });

        var usage = await h.UsageAsync(recorded.UsageRecordId!.Value);
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, usage.ProviderBilling);
        Assert.Equal(0.05m, usage.ProviderCost); // Omni's cost, recorded for margin reporting
        Assert.Equal(10m, (await h.AccountAsync(client)).WalletBalance);
    }

    [PostgresFact]
    public async Task UnchargedUsage_IsIdempotent_AndItsOutcomeIsRecordedOnce()
    {
        var (h, client, card, evt) = await SetupAsync();
        await h.AddRateAsync(card, evt, clientRate: 0.07m, providerCost: 0.05m);
        var e = h.Event(client, evt, quantity: 2) with { ProviderBilling = ProviderBillingResponsibility.CustomerDirect };

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => h.Billing.ReserveAsync(e)));
        Assert.Single(results.Select(r => r.UsageRecordId).Distinct());

        var first = await h.Billing.SettleAsync(client, e.IdempotencyKey, finalQuantity: 3);
        var again = await h.Billing.SettleAsync(client, e.IdempotencyKey);
        var lateFailure = await h.Billing.ReleaseAsync(client, e.IdempotencyKey, "late failure");

        Assert.Equal(UsageOutcome.Recorded, first.Outcome);
        Assert.True(again.Duplicate);
        Assert.Equal(UsageOutcome.Conflict, lateFailure.Outcome);
        var usage = await h.UsageAsync(first.UsageRecordId!.Value);
        Assert.Equal(3m, usage.Quantity);
        Assert.Equal(0.15m, usage.ProviderCost);
        Assert.Equal(ProviderOutcome.Billable, usage.ProviderOutcome);
        Assert.Equal(0m, usage.Amount);
        await h.AssertReconciledAsync(client);
    }

    [PostgresFact]
    public async Task AccountDefaults_AndOverrides_ResolvePerConnectedAccount()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var whatsapp = await h.ConnectChannelAsync(client, ChannelType.WhatsApp);
        await h.ConnectChannelAsync(client, ChannelType.Telegram);

        var waDefault = await h.ProviderBilling.ResolveAsync(client, ChannelType.WhatsApp, ChannelProvider.Meta);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, waDefault.Responsibility); // unchanged behaviour
        Assert.True(waDefault.ChargesUsage);
        Assert.Equal(whatsapp, waDefault.ChannelIntegrationId);
        var telegram = await h.ProviderBilling.ResolveAsync(client, ChannelType.Telegram, null);
        Assert.Equal(ProviderBillingResponsibility.NoProviderUsageFee, telegram.Responsibility);
        Assert.False(telegram.ChargesUsage);

        await Assert.ThrowsAsync<ArgumentException>(() => h.ProviderBilling.SetAsync(whatsapp,
            new ChannelAccountBillingChange(ProviderBillingResponsibility.CustomerDirect, null, " ", "tests"))); // reason required
        await Assert.ThrowsAsync<ArgumentException>(() => h.ProviderBilling.SetAsync(whatsapp,
            new ChannelAccountBillingChange("meta_pays", null, "typo", "tests")));

        await h.ProviderBilling.SetAsync(whatsapp, new ChannelAccountBillingChange(ProviderBillingResponsibility.CustomerDirect, null,
            "Client's own WABA with its own Meta payment method", "ops@omni"));
        var overridden = await h.ProviderBilling.ResolveAsync(client, ChannelType.WhatsApp, ChannelProvider.Meta);
        Assert.Equal(ProviderBillingResponsibility.CustomerDirect, overridden.Responsibility);
        Assert.False(overridden.ChargesUsage);
        Assert.Equal(ProviderBillingService.SourceAccount, overridden.Source);

        var reset = await h.ProviderBilling.ResetAsync(whatsapp, "back to defaults", "ops@omni");
        Assert.Equal(ProviderBillingService.SourceDefault, reset!.Source);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, reset.ProviderBilling);
    }

    [PostgresFact]
    public async Task TheReport_SeparatesCostPaidByPlatformFromCostPaidExternally()
    {
        var (h, client, card, evt) = await SetupAsync(wallet: 10m);
        await h.AddRateAsync(card, evt, clientRate: 0.065m, providerCost: 0.05m);

        await h.Billing.ChargeAsync(h.Event(client, evt) with { ProviderBilling = ProviderBillingResponsibility.PlatformFunded });
        await h.Billing.ChargeAsync(h.Event(client, evt) with { ProviderBilling = ProviderBillingResponsibility.CustomerDirect });

        var report = await h.Queries.GetReportAsync(h.Time.Now.AddDays(-1), h.Time.Now.AddDays(1));
        var funded = Assert.Single(report.Usage, r => r.ClientId == client && r.ProviderBilling == ProviderBillingResponsibility.PlatformFunded);
        var direct = Assert.Single(report.Usage, r => r.ClientId == client && r.ProviderBilling == ProviderBillingResponsibility.CustomerDirect);
        Assert.Equal(0.065m, funded.Revenue);
        Assert.Equal(0.05m, funded.ProviderCost);
        Assert.Equal(0.015m, funded.Margin);
        Assert.Equal(0m, direct.Revenue);
        Assert.Equal(0.05m, direct.ProviderCost);
        Assert.Equal(0m, direct.Margin); // Omni neither paid nor charged
        Assert.True(report.ProviderCostPaidExternally >= 0.05m);
        Assert.Equal(9.935m, (await h.AccountAsync(client)).WalletBalance);
        await h.AssertReconciledAsync(client);
    }
}
