using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Billing;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Tests;

/// <summary>Wallet, reservation, settlement, release, refunds, idempotency and concurrency — against real PostgreSQL.</summary>
[Collection("Postgres")]
public class WalletAndUsageTests
{
    private readonly PostgresFixture _db;

    public WalletAndUsageTests(PostgresFixture db) => _db = db;

    private async Task<(BillingHarness H, Guid Clinic, string EventType)> SetupAsync(decimal price = 1m, decimal providerCost = 0.4m, decimal topUp = 0m)
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        var eventType = BillingHarness.Unique("evt");
        await h.AddRateAsync(card, eventType, price, providerCost);
        if (topUp > 0) await h.TopUpAsync(clinic, topUp);
        return (h, clinic, eventType);
    }

    [PostgresFact]
    public async Task TopUp_AddsToTheWallet_AndRepeatingTheSameKeyDoesNothing()
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var request = new WalletTopUp(clinic, 100m, "payment-123", "bank transfer 123", "Initial deposit", BillingSource.Admin, "ops@sculptflow");

        var first = await h.Billing.TopUpAsync(request);
        var again = await h.Billing.TopUpAsync(request);

        Assert.False(first.Duplicate);
        Assert.True(again.Duplicate);
        Assert.Equal(first.LedgerEntryId, again.LedgerEntryId);
        Assert.Equal(100m, (await h.AccountAsync(clinic)).WalletBalance);
        var entry = Assert.Single(await h.LedgerAsync(clinic));
        Assert.Equal(LedgerEntryType.WalletTopUp, entry.EntryType);
        Assert.Equal("ops@sculptflow", entry.Actor);
        Assert.Equal("bank transfer 123", entry.Reference);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Billing.TopUpAsync(request with { Amount = 50m }));
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Reserve_HoldsTheRatedAmount_AndReducesSpendable()
    {
        var (h, clinic, evt) = await SetupAsync(price: 10m, topUp: 100m);
        var result = await h.Billing.ReserveAsync(h.Event(clinic, evt));

        Assert.Equal(UsageOutcome.Reserved, result.Outcome);
        Assert.Equal(10m, result.Amount);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(100m, account.WalletBalance);
        Assert.Equal(10m, account.ReservedAmount);
        Assert.Equal(90m, account.Spendable);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Reserve_WithoutEnoughBalance_IsRefused_AndRecordedAsFailed_WithoutChargingAnything()
    {
        var (h, clinic, evt) = await SetupAsync(price: 10m, topUp: 25m);
        await h.Billing.ReserveAsync(h.Event(clinic, evt));
        await h.Billing.ReserveAsync(h.Event(clinic, evt));

        var refused = await h.Billing.ReserveAsync(h.Event(clinic, evt)); // 5 left, costs 10

        Assert.Equal(UsageOutcome.Failed, refused.Outcome);
        Assert.Equal(UsageFailureReason.InsufficientFunds, refused.FailureReason);
        var failed = await h.UsageAsync(refused.UsageRecordId!.Value);
        Assert.Equal(ChargeStatus.Failed, failed.ChargeStatus);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(20m, account.ReservedAmount);
        Assert.Equal(25m, account.WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task ConcurrentReservations_NeverOverspendTheWallet()
    {
        var (h, clinic, evt) = await SetupAsync(price: 1m, topUp: 20m);

        // 50 workers race for $1 each against a $20 balance.
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => h.Billing.ReserveAsync(h.Event(clinic, evt))));

        Assert.Equal(20, results.Count(r => r.Outcome == UsageOutcome.Reserved));
        Assert.Equal(30, results.Count(r => r.FailureReason == UsageFailureReason.InsufficientFunds));
        var account = await h.AccountAsync(clinic);
        Assert.Equal(20m, account.ReservedAmount);
        Assert.Equal(0m, account.Spendable);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task TheSameEvent_ReservedTwice_OrConcurrently_IsOneReservation()
    {
        var (h, clinic, evt) = await SetupAsync(price: 2m, topUp: 50m);
        var e = h.Event(clinic, evt, key: "whatsapp:message:" + Guid.NewGuid());

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => h.Billing.ReserveAsync(e)));

        Assert.Single(results.Select(r => r.UsageRecordId).Distinct());
        Assert.Equal(9, results.Count(r => r.Duplicate));
        Assert.Equal(2m, (await h.AccountAsync(clinic)).ReservedAmount);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Release_ReturnsTheHold_AndIsIdempotent()
    {
        var (h, clinic, evt) = await SetupAsync(price: 7m, topUp: 10m);
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);

        var released = await h.Billing.ReleaseAsync(clinic, e.IdempotencyKey, "send_failed");
        var again = await h.Billing.ReleaseAsync(clinic, e.IdempotencyKey, "send_failed");

        Assert.Equal(UsageOutcome.Released, released.Outcome);
        Assert.True(again.Duplicate);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(0m, account.ReservedAmount);
        Assert.Equal(10m, account.WalletBalance);
        Assert.DoesNotContain((await h.LedgerAsync(clinic)), l => l.EntryType != LedgerEntryType.WalletTopUp);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Settle_FromWalletOnly_DebitsTheWallet_AndRecordsCostAndPrice()
    {
        var (h, clinic, evt) = await SetupAsync(price: 0.05m, providerCost: 0.03m, topUp: 10m);
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);

        var settled = await h.Billing.SettleAsync(clinic, e.IdempotencyKey);

        Assert.Equal(UsageOutcome.Settled, settled.Outcome);
        var usage = await h.UsageAsync(settled.UsageRecordId!.Value);
        Assert.Equal(0.05m, usage.Amount);
        Assert.Equal(0.03m, usage.ProviderCost);
        Assert.Equal(0.05m, usage.UnitPrice);
        Assert.Equal(0.05m, usage.WalletAmount);
        Assert.Equal(0m, usage.CreditAmount);
        Assert.Equal(RateSource.Client, usage.RateSource);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(9.95m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        Assert.Contains(await h.LedgerAsync(clinic), l => l.EntryType == LedgerEntryType.UsageDebit && l.Amount == -0.05m && l.UsageRecordId == usage.Id);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Settle_FromIncludedCreditOnly_LeavesTheWalletUntouched()
    {
        var (h, clinic, evt) = await SetupAsync(price: 2m);
        await h.Billing.AdjustAsync(new WalletAdjustment(clinic, 10m, LedgerBalanceType.IncludedCredit, "test credit", Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);
        await h.Billing.SettleAsync(clinic, e.IdempotencyKey);

        var account = await h.AccountAsync(clinic);
        Assert.Equal(8m, account.IncludedCreditBalance);
        Assert.Equal(0m, account.WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Settle_UsesIncludedCreditFirst_ThenTheWallet_AndRecordsBothInTheLedger()
    {
        // Credit $3, wallet $10, usage $5 -> $3 from credit + $2 from wallet.
        var (h, clinic, evt) = await SetupAsync(price: 5m, topUp: 10m);
        await h.Billing.AdjustAsync(new WalletAdjustment(clinic, 3m, LedgerBalanceType.IncludedCredit, "test credit", Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);

        var settled = await h.Billing.SettleAsync(clinic, e.IdempotencyKey);

        var usage = await h.UsageAsync(settled.UsageRecordId!.Value);
        Assert.Equal(3m, usage.CreditAmount);
        Assert.Equal(2m, usage.WalletAmount);
        var ledger = await h.LedgerAsync(clinic);
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.IncludedCreditConsumption && l.Amount == -3m && l.BalanceAfter == 0m);
        Assert.Contains(ledger, l => l.EntryType == LedgerEntryType.UsageDebit && l.Amount == -2m && l.BalanceAfter == 8m);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(0m, account.IncludedCreditBalance);
        Assert.Equal(8m, account.WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task DuplicateAndConcurrentSettlements_ChargeExactlyOnce()
    {
        var (h, clinic, evt) = await SetupAsync(price: 4m, topUp: 20m);
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => h.Billing.SettleAsync(clinic, e.IdempotencyKey)));

        Assert.Equal(1, results.Count(r => r.Outcome == UsageOutcome.Settled && !r.Duplicate));
        Assert.Equal(9, results.Count(r => r.Duplicate));
        Assert.Single((await h.LedgerAsync(clinic)), l => l.EntryType == LedgerEntryType.UsageDebit);
        Assert.Equal(16m, (await h.AccountAsync(clinic)).WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task SettlementAfterRelease_AndReleaseAfterSettlement_ChangeNothing()
    {
        var (h, clinic, evt) = await SetupAsync(price: 3m, topUp: 10m);
        var released = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(released);
        await h.Billing.ReleaseAsync(clinic, released.IdempotencyKey, "delivery_failed");
        Assert.Equal(UsageOutcome.Conflict, (await h.Billing.SettleAsync(clinic, released.IdempotencyKey)).Outcome);

        var settled = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(settled);
        await h.Billing.SettleAsync(clinic, settled.IdempotencyKey);
        Assert.Equal(UsageOutcome.Conflict, (await h.Billing.ReleaseAsync(clinic, settled.IdempotencyKey, "late failure")).Outcome);

        var account = await h.AccountAsync(clinic);
        Assert.Equal(7m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        Assert.Equal(UsageOutcome.NotFound, (await h.Billing.SettleAsync(clinic, "never-reserved")).Outcome);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Settle_ForLessThanReserved_ChargesTheFinalAmount_AndReturnsTheRest()
    {
        var (h, clinic, evt) = await SetupAsync(price: 0.5m, topUp: 10m);
        var e = h.Event(clinic, evt, quantity: 10); // e.g. 10 estimated voice minutes = $5 held
        await h.Billing.ReserveAsync(e);
        Assert.Equal(5m, (await h.AccountAsync(clinic)).ReservedAmount);

        var settled = await h.Billing.SettleAsync(clinic, e.IdempotencyKey, finalQuantity: 3); // 3 minutes actually used

        Assert.Equal(1.5m, settled.Amount);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(8.5m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        var usage = await h.UsageAsync(settled.UsageRecordId!.Value);
        Assert.Equal(3m, usage.Quantity);
        Assert.Equal(5m, usage.ReservedAmount); // what was held stays on record
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Settle_ForMoreThanReserved_ChargesTheFinalAmount_EvenIntoOverdraft()
    {
        var (h, clinic, evt) = await SetupAsync(price: 1m, topUp: 3m);
        var e = h.Event(clinic, evt, quantity: 2); // $2 held of $3
        await h.Billing.ReserveAsync(e);

        var settled = await h.Billing.SettleAsync(clinic, e.IdempotencyKey, finalQuantity: 5); // turned out $5

        Assert.Equal(5m, settled.Amount);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(-2m, account.WalletBalance); // the usage happened; the shortfall is visible, not lost
        Assert.Equal(0m, account.ReservedAmount);
        // ...and nothing new can be reserved until it's topped up.
        Assert.Equal(UsageOutcome.Failed, (await h.Billing.ReserveAsync(h.Event(clinic, evt))).Outcome);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task ACrashBeforeCommit_LeavesNothingHalfDone_AndTheRetrySettlesOnce()
    {
        var (h, clinic, evt) = await SetupAsync(price: 6m, topUp: 10m);
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);

        var crashing = new BillingHarness(_db.ConnectionString, interceptors: new CrashAfterSaveInterceptor());
        await Assert.ThrowsAsync<InvalidOperationException>(() => crashing.Billing.SettleAsync(clinic, e.IdempotencyKey));

        // Rolled back: still reserved, nothing debited, no ledger row.
        await using (var db = h.Db())
        {
            var usage = await db.BillingUsageRecords.AsNoTracking().SingleAsync(u => u.ClinicId == clinic && u.IdempotencyKey == e.IdempotencyKey);
            Assert.Equal(ChargeStatus.Reserved, usage.ChargeStatus);
        }
        var account = await h.AccountAsync(clinic);
        Assert.Equal(10m, account.WalletBalance);
        Assert.Equal(6m, account.ReservedAmount);
        Assert.DoesNotContain(await h.LedgerAsync(clinic), l => l.EntryType == LedgerEntryType.UsageDebit);
        await h.AssertReconciledAsync(clinic);

        // The retry (e.g. the provider re-sending the callback) settles it exactly once.
        Assert.Equal(UsageOutcome.Settled, (await h.Billing.SettleAsync(clinic, e.IdempotencyKey)).Outcome);
        Assert.True((await h.Billing.SettleAsync(clinic, e.IdempotencyKey)).Duplicate);
        Assert.Equal(4m, (await h.AccountAsync(clinic)).WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Charge_RatesAndSettlesInOneStep_StillPrepaid()
    {
        var (h, clinic, evt) = await SetupAsync(price: 3m, topUp: 5m);
        var ok = await h.Billing.ChargeAsync(h.Event(clinic, evt));
        var refused = await h.Billing.ChargeAsync(h.Event(clinic, evt));

        Assert.Equal(UsageOutcome.Settled, ok.Outcome);
        Assert.Equal(UsageOutcome.Failed, refused.Outcome);
        Assert.Equal(2m, (await h.AccountAsync(clinic)).WalletBalance);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task Refund_ReturnsTheMoneyToWhereItCameFrom_Once()
    {
        var (h, clinic, evt) = await SetupAsync(price: 5m, topUp: 10m);
        await h.Billing.AdjustAsync(new WalletAdjustment(clinic, 3m, LedgerBalanceType.IncludedCredit, "test credit", Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));
        var e = h.Event(clinic, evt);
        await h.Billing.ReserveAsync(e);
        var settled = await h.Billing.SettleAsync(clinic, e.IdempotencyKey);

        var refund = await h.Billing.RefundAsync(clinic, settled.UsageRecordId!.Value, "Customer complaint #12", BillingSource.Admin, "ops");
        var again = await h.Billing.RefundAsync(clinic, settled.UsageRecordId!.Value, "Customer complaint #12", BillingSource.Admin, "ops");

        Assert.False(refund.Duplicate);
        Assert.True(again.Duplicate);
        var account = await h.AccountAsync(clinic);
        Assert.Equal(3m, account.IncludedCreditBalance);
        Assert.Equal(10m, account.WalletBalance);
        Assert.Equal(2, (await h.LedgerAsync(clinic)).Count(l => l.EntryType == LedgerEntryType.UsageRefund));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Billing.RefundAsync(clinic, settled.UsageRecordId!.Value, " ", BillingSource.Admin, "ops"));
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task ManualAdjustments_NeedAReason_AndCantTakeBalancesBelowZero()
    {
        var (h, clinic, evt) = await SetupAsync(price: 4m, topUp: 10m);
        await h.Billing.ReserveAsync(h.Event(clinic, evt)); // $4 held

        await Assert.ThrowsAsync<ArgumentException>(() => h.Billing.AdjustAsync(
            new WalletAdjustment(clinic, -1m, LedgerBalanceType.Wallet, "", "k1", BillingSource.Admin, "ops")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Billing.AdjustAsync(
            new WalletAdjustment(clinic, -7m, LedgerBalanceType.Wallet, "goodwill reversal", "k2", BillingSource.Admin, "ops"))); // would uncover the hold

        var ok = await h.Billing.AdjustAsync(new WalletAdjustment(clinic, -6m, LedgerBalanceType.Wallet, "bank charge-back", "k3", BillingSource.Admin, "ops"));
        Assert.Equal(4m, ok.BalanceAfter);
        var entry = (await h.LedgerAsync(clinic)).Single(l => l.EntryType == LedgerEntryType.ManualAdjustment);
        Assert.Equal("bank charge-back", entry.Reason);
        Assert.Equal("ops", entry.Actor);
        await h.AssertReconciledAsync(clinic);
    }

    [PostgresFact]
    public async Task UsageRecords_KeepEverythingNeededForReporting()
    {
        var (h, clinic, evt) = await SetupAsync(price: 0.08m, providerCost: 0.05m, topUp: 10m);
        var messageId = Guid.NewGuid();
        var e = h.Event(clinic, evt, country: "lb", provider: "Infobip") with { MessageId = messageId, Channel = "whatsapp" };
        var reserved = await h.Billing.ReserveAsync(e);
        await h.Billing.SettleAsync(clinic, e.IdempotencyKey);

        var usage = await h.UsageAsync(reserved.UsageRecordId!.Value);
        Assert.Equal("LB", usage.CountryCode);
        Assert.Equal("infobip", usage.Provider);
        Assert.Equal("whatsapp", usage.Channel);
        Assert.Equal(messageId, usage.MessageId);
        Assert.Equal("USD", usage.Currency.Trim());
        Assert.NotNull(usage.RateId);
        Assert.NotNull(usage.ReservedAt);
        Assert.NotNull(usage.SettledAt);

        var report = await h.Queries.GetReportAsync(h.Time.Now.AddDays(-1), h.Time.Now.AddDays(1));
        var row = Assert.Single(report.Usage, r => r.ClinicId == clinic);
        Assert.Equal(0.08m, row.Revenue);
        Assert.Equal(0.05m, row.ProviderCost);
        Assert.Equal(0.03m, row.Margin);
    }
}
