using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Billing;

/// <summary>
/// The billing core: usage recording, rating, reservation, settlement, release, refunds and wallet top-ups/adjustments,
/// for any channel. It knows nothing about WhatsApp/SMS/... — channels hand it normalized <see cref="BillableEvent"/>s
/// (see IChannelBillingPolicy / MessageBillingService), already carrying who pays the provider and whether SculptFlow
/// charges for the usage.
///
///   SculptFlow charges it:  Reserve (rate it, hold funds) ──> Settle (charge: included credit first, then wallet)
///                                                         └─> Release (not billable after all: hold returned)
///   SculptFlow doesn't:     Record (usage + provider cost where known; no rate needed, no hold, no wallet, no ledger)
///                           ──> Settle / Release only update the provider outcome
///
/// Prepaid: a reservation needs included credit + wallet - already reserved >= its amount. Every operation is one
/// transaction holding the clinic's billing account row lock, and is idempotent by key (see BillingStore).
/// </summary>
public interface IBillingService
{
    /// <summary>Records the event. When SculptFlow charges it (<see cref="BillableEvent.ChargesUsage"/>): rates it
    /// and holds its price; Failed (nothing held) when there's no rate or not enough balance — the failure is recorded
    /// as its own usage row. When it doesn't: records it as not charged (Recorded), with the provider cost when a rate
    /// knows it; never fails for a missing rate. A key seen before returns that record unchanged.</summary>
    Task<UsageResult> ReserveAsync(BillableEvent billableEvent, CancellationToken ct = default);

    /// <summary>The usage became billable at the provider. If SculptFlow reserved for it: charges it (at the reserved
    /// unit price, for finalQuantity units when given, else the reserved quantity) and returns any unused hold. If it
    /// isn't charged: only records the outcome (and final quantity). Settling twice is a no-op; settling a released
    /// record does nothing (Conflict).</summary>
    Task<UsageResult> SettleAsync(Guid clinicId, string idempotencyKey, decimal? finalQuantity = null, CancellationToken ct = default);

    /// <summary>The usage turned out not billable at the provider: returns the hold (or, for uncharged usage, just
    /// records the outcome). Idempotent; never undoes a settlement.</summary>
    Task<UsageResult> ReleaseAsync(Guid clinicId, string idempotencyKey, string reason, CancellationToken ct = default);

    /// <summary>Records and settles in one step, for usage that is billable the moment it is known (no hold first).
    /// Charged usage is still prepaid: fails when the balance can't cover it.</summary>
    Task<UsageResult> ChargeAsync(BillableEvent billableEvent, CancellationToken ct = default);

    /// <summary>Gives a settled usage's full amount back to where it was paid from (credit and/or wallet). Once only.</summary>
    Task<UsageResult> RefundAsync(Guid clinicId, Guid usageRecordId, string reason, string source, string? actor, CancellationToken ct = default);

    Task<LedgerResult> TopUpAsync(WalletTopUp request, CancellationToken ct = default);

    /// <summary>Manual correction with a mandatory reason. A debit can't take the balance or the spendable amount below zero.</summary>
    Task<LedgerResult> AdjustAsync(WalletAdjustment request, CancellationToken ct = default);
}

public class BillingService : IBillingService
{
    /// <summary>Idempotency keys longer than this are rejected (the column is 200; failed attempts append a suffix).</summary>
    public const int MaxKeyLength = 150;

    private readonly BillingDbFactory _dbFactory;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BillingService> _logger;

    public BillingService(BillingDbFactory dbFactory, IOptions<BillingOptions> options, TimeProvider time, ILogger<BillingService> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public Task<UsageResult> ReserveAsync(BillableEvent billableEvent, CancellationToken ct = default) =>
        RecordUsageAsync(billableEvent, settleImmediately: false, ct);

    public Task<UsageResult> ChargeAsync(BillableEvent billableEvent, CancellationToken ct = default) =>
        RecordUsageAsync(billableEvent, settleImmediately: true, ct);

    private async Task<UsageResult> RecordUsageAsync(BillableEvent e, bool settleImmediately, CancellationToken ct)
    {
        Validate(e);
        var now = _time.GetUtcNow();
        var occurredAt = e.OccurredAt ?? now;

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, e.ClinicId, _options.NormalizedCurrency, ct);

        var existing = await db.BillingUsageRecords.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ClinicId == e.ClinicId && u.IdempotencyKey == e.IdempotencyKey, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Billing: duplicate usage {IdempotencyKey} for clinic {ClinicId} ignored (already {Status}).",
                e.IdempotencyKey, e.ClinicId, existing.ChargeStatus);
            return ToResult(existing, duplicate: true);
        }

        var usage = new BillingUsageRecord
        {
            Id = Guid.NewGuid(),
            ClinicId = e.ClinicId,
            BillingAccountId = account.Id,
            IdempotencyKey = e.IdempotencyKey,
            EventType = e.EventType,
            Channel = e.Channel.Trim().ToLowerInvariant(),
            ChannelIntegrationId = e.ChannelIntegrationId,
            Quantity = e.Quantity,
            Unit = BillingStore.Truncate(e.Unit, 20),
            CountryCode = RateResolver.NormalizeCountry(e.CountryCode),
            Operator = BillingStore.Truncate(RateResolver.NormalizeDimension(e.Operator), 60),
            Provider = BillingStore.Truncate(RateResolver.NormalizeDimension(e.Provider), 30),
            ProviderBilling = e.ProviderBilling,
            ProviderOutcome = settleImmediately ? ProviderOutcome.Billable : ProviderOutcome.Pending,
            Currency = account.Currency,
            MessageId = e.MessageId,
            ConversationId = e.ConversationId,
            CampaignId = e.CampaignId,
            Source = e.Source,
            Actor = BillingStore.Truncate(e.Actor, 200),
            OccurredAt = occurredAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (!e.ChargesUsage)
        {
            return await SaveNotChargedAsync(db, tx, usage, occurredAt, ct);
        }

        var quote = await RateResolver.ResolveAsync(db,
            new RatingRequest(e.ClinicId, e.EventType, usage.CountryCode, usage.Operator, usage.Provider, occurredAt, e.ProviderBilling), ct);
        if (quote is null)
        {
            _logger.LogWarning("Billing: no rate for {EventType} (country {Country}, provider {Provider}, paid by {ProviderBilling}) for clinic {ClinicId}; usage {IdempotencyKey} refused.",
                e.EventType, usage.CountryCode ?? "-", usage.Provider ?? "-", e.ProviderBilling, e.ClinicId, e.IdempotencyKey);
            return await SaveFailedAsync(db, tx, usage, UsageFailureReason.RateNotFound, ct);
        }
        if (!string.Equals(quote.Currency, account.Currency, StringComparison.Ordinal))
        {
            _logger.LogError("Billing: rate {RateId} is in {RateCurrency} but clinic {ClinicId}'s account is in {AccountCurrency}; usage {IdempotencyKey} refused.",
                quote.RateId, quote.Currency, e.ClinicId, account.Currency, e.IdempotencyKey);
            return await SaveFailedAsync(db, tx, usage, UsageFailureReason.CurrencyMismatch, ct);
        }

        ApplyQuote(usage, quote);
        var amount = usage.Amount!.Value;

        if (amount > account.Spendable)
        {
            _logger.LogWarning("Billing: insufficient balance for clinic {ClinicId}: {EventType} costs {Amount} {Currency}, spendable {Spendable}; usage {IdempotencyKey} refused.",
                e.ClinicId, e.EventType, amount, account.Currency, account.Spendable, e.IdempotencyKey);
            return await SaveFailedAsync(db, tx, usage, UsageFailureReason.InsufficientFunds, ct);
        }

        db.BillingUsageRecords.Add(usage);
        if (settleImmediately)
        {
            await ApplySettlementAsync(db, account, usage, usage.Quantity, now, ct);
        }
        else
        {
            usage.ChargeStatus = ChargeStatus.Reserved;
            usage.ReservedAmount = amount;
            usage.ReservedAt = now;
            account.ReservedAmount = BillingStore.RoundMoney(account.ReservedAmount + amount);
            account.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: {Action} {Amount} {Currency} for {EventType} x{Quantity} (rate {RateId}, {RateSource}) for clinic {ClinicId}, usage {IdempotencyKey}.",
            settleImmediately ? "charged" : "reserved", amount, account.Currency, e.EventType, e.Quantity, quote.RateId, quote.RateSource, e.ClinicId, e.IdempotencyKey);
        return ToResult(usage, duplicate: false);
    }

    public async Task<UsageResult> SettleAsync(Guid clinicId, string idempotencyKey, decimal? finalQuantity = null, CancellationToken ct = default)
    {
        if (finalQuantity is < 0) throw new ArgumentOutOfRangeException(nameof(finalQuantity), "Quantity can't be negative.");
        var now = _time.GetUtcNow();

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, clinicId, _options.NormalizedCurrency, ct);

        var usage = await db.BillingUsageRecords.FirstOrDefaultAsync(u => u.ClinicId == clinicId && u.IdempotencyKey == idempotencyKey, ct);
        if (usage is null)
        {
            return new UsageResult(UsageOutcome.NotFound, null, null, null, false);
        }
        switch (usage.ChargeStatus)
        {
            case ChargeStatus.NotCharged:
                // Not SculptFlow's money: only the provider outcome (and final quantity/cost) is recorded.
                if (usage.ProviderOutcome != ProviderOutcome.Pending)
                {
                    return usage.ProviderOutcome == ProviderOutcome.Billable
                        ? ToResult(usage, duplicate: true)
                        : new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, null, false);
                }
                if (finalQuantity is decimal q)
                {
                    usage.Quantity = q;
                    if (usage.UnitProviderCost is decimal unitCost) usage.ProviderCost = BillingStore.RoundMoney(unitCost * q);
                }
                usage.ProviderOutcome = ProviderOutcome.Billable;
                usage.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ToResult(usage, duplicate: false);
            case ChargeStatus.Settled:
                _logger.LogInformation("Billing: duplicate settlement of usage {IdempotencyKey} for clinic {ClinicId} ignored.", idempotencyKey, clinicId);
                return ToResult(usage, duplicate: true);
            case ChargeStatus.Released:
                _logger.LogWarning("Billing: usage {IdempotencyKey} for clinic {ClinicId} became billable after it was released ({Reason}); not charged.",
                    idempotencyKey, clinicId, usage.ReleaseReason);
                return new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, null, false);
            case ChargeStatus.Failed:
                return new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, usage.FailureReason, false);
        }

        // Return the whole hold first, then charge the final amount — so a final amount that differs from the
        // estimate (lower, or higher when it couldn't be known up front) is handled by the same arithmetic.
        ReleaseHold(account, usage, now);
        var finalAmount = await ApplySettlementAsync(db, account, usage, finalQuantity ?? usage.Quantity, now, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (finalAmount > usage.ReservedAmount)
        {
            _logger.LogInformation("Billing: usage {IdempotencyKey} for clinic {ClinicId} settled at {Final}, above its {Reserved} reservation.",
                idempotencyKey, clinicId, finalAmount, usage.ReservedAmount);
        }
        if (account.WalletBalance < 0)
        {
            _logger.LogWarning("Billing: clinic {ClinicId}'s wallet is overdrawn ({Wallet} {Currency}) after settling usage {IdempotencyKey}.",
                clinicId, account.WalletBalance, account.Currency, idempotencyKey);
        }
        _logger.LogInformation("Billing: settled usage {IdempotencyKey} for clinic {ClinicId}: {Amount} {Currency} ({Credit} from included credit, {Wallet} from wallet).",
            idempotencyKey, clinicId, finalAmount, account.Currency, usage.CreditAmount, usage.WalletAmount);
        return ToResult(usage, duplicate: false);
    }

    public async Task<UsageResult> ReleaseAsync(Guid clinicId, string idempotencyKey, string reason, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, clinicId, _options.NormalizedCurrency, ct);

        var usage = await db.BillingUsageRecords.FirstOrDefaultAsync(u => u.ClinicId == clinicId && u.IdempotencyKey == idempotencyKey, ct);
        if (usage is null)
        {
            return new UsageResult(UsageOutcome.NotFound, null, null, null, false);
        }
        switch (usage.ChargeStatus)
        {
            case ChargeStatus.NotCharged:
                if (usage.ProviderOutcome != ProviderOutcome.Pending)
                {
                    return usage.ProviderOutcome == ProviderOutcome.NotBillable
                        ? ToResult(usage, duplicate: true)
                        : new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, null, false);
                }
                usage.ProviderOutcome = ProviderOutcome.NotBillable;
                usage.ReleaseReason = BillingStore.Truncate(reason, 100);
                usage.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ToResult(usage, duplicate: false);
            case ChargeStatus.Released:
                _logger.LogInformation("Billing: duplicate release of usage {IdempotencyKey} for clinic {ClinicId} ignored.", idempotencyKey, clinicId);
                return ToResult(usage, duplicate: true);
            case ChargeStatus.Settled:
                _logger.LogWarning("Billing: release of usage {IdempotencyKey} for clinic {ClinicId} ignored — it was already settled ({Reason}).",
                    idempotencyKey, clinicId, reason);
                return new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, null, false);
            case ChargeStatus.Failed:
                return new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, usage.FailureReason, false);
        }

        ReleaseHold(account, usage, now);
        usage.ChargeStatus = ChargeStatus.Released;
        usage.ProviderOutcome = ProviderOutcome.NotBillable;
        usage.ReleaseReason = BillingStore.Truncate(reason, 100);
        usage.ReleasedAt = now;
        usage.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: released {Amount} {Currency} held for usage {IdempotencyKey} of clinic {ClinicId} ({Reason}).",
            usage.ReservedAmount, account.Currency, idempotencyKey, clinicId, reason);
        return ToResult(usage, duplicate: false);
    }

    public async Task<UsageResult> RefundAsync(Guid clinicId, Guid usageRecordId, string reason, string source, string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required for a refund.", nameof(reason));
        var now = _time.GetUtcNow();

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, clinicId, _options.NormalizedCurrency, ct);

        var usage = await db.BillingUsageRecords.FirstOrDefaultAsync(u => u.ClinicId == clinicId && u.Id == usageRecordId, ct);
        if (usage is null) return new UsageResult(UsageOutcome.NotFound, null, null, null, false);
        if (usage.ChargeStatus != ChargeStatus.Settled)
        {
            return new UsageResult(UsageOutcome.Conflict, usage.Id, usage.Amount, usage.FailureReason, false);
        }
        if (usage.RefundedAt is not null)
        {
            _logger.LogInformation("Billing: duplicate refund of usage {UsageId} for clinic {ClinicId} ignored.", usageRecordId, clinicId);
            return ToResult(usage, duplicate: true);
        }

        var context = new LedgerContext(source, actor, reason.Trim(), UsageRecordId: usage.Id);
        if (usage.CreditAmount > 0)
        {
            await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.UsageRefund, LedgerBalanceType.IncludedCredit,
                usage.CreditAmount, $"usage:{usage.Id}:refund:credit", context, now, ct);
        }
        if (usage.WalletAmount > 0)
        {
            await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.UsageRefund, LedgerBalanceType.Wallet,
                usage.WalletAmount, $"usage:{usage.Id}:refund:wallet", context, now, ct);
        }
        usage.RefundedAmount = usage.Amount ?? 0;
        usage.RefundedAt = now;
        usage.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: refunded usage {UsageId} ({Amount} {Currency}) for clinic {ClinicId} by {Actor}: {Reason}.",
            usageRecordId, usage.RefundedAmount, account.Currency, clinicId, actor ?? source, reason);
        return ToResult(usage, duplicate: false);
    }

    public async Task<LedgerResult> TopUpAsync(WalletTopUp request, CancellationToken ct = default)
    {
        if (request.Amount <= 0) throw new ArgumentException("A top-up amount must be greater than zero.", nameof(request));
        ValidateKey(request.IdempotencyKey);
        var now = _time.GetUtcNow();
        var key = "topup:" + request.IdempotencyKey;

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, request.ClinicId, _options.NormalizedCurrency, ct);

        var existing = await db.BillingLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ClinicId == request.ClinicId && l.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.Amount != BillingStore.RoundMoney(request.Amount))
            {
                throw new InvalidOperationException("This idempotency key was already used for a different top-up amount.");
            }
            _logger.LogInformation("Billing: duplicate top-up {Key} for clinic {ClinicId} ignored.", request.IdempotencyKey, request.ClinicId);
            return new LedgerResult(existing.Id, existing.BalanceAfter, Duplicate: true);
        }

        var entry = await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.WalletTopUp, LedgerBalanceType.Wallet, request.Amount, key,
            new LedgerContext(request.Source, request.Actor, request.Reason, Reference: request.Reference, CorrelationId: request.IdempotencyKey), now, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: wallet top-up of {Amount} {Currency} for clinic {ClinicId} by {Actor} (reference {Reference}); wallet now {Wallet}.",
            entry.Amount, account.Currency, request.ClinicId, request.Actor ?? request.Source, request.Reference ?? "-", account.WalletBalance);
        return new LedgerResult(entry.Id, entry.BalanceAfter, Duplicate: false);
    }

    public async Task<LedgerResult> AdjustAsync(WalletAdjustment request, CancellationToken ct = default)
    {
        if (request.Amount == 0) throw new ArgumentException("An adjustment amount can't be zero.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("A reason is required for a manual adjustment.", nameof(request));
        if (request.BalanceType is not (LedgerBalanceType.Wallet or LedgerBalanceType.IncludedCredit))
        {
            throw new ArgumentException("Balance type must be 'wallet' or 'included_credit'.", nameof(request));
        }
        ValidateKey(request.IdempotencyKey);
        var now = _time.GetUtcNow();
        var key = "adjustment:" + request.IdempotencyKey;
        var amount = BillingStore.RoundMoney(request.Amount);

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, request.ClinicId, _options.NormalizedCurrency, ct);

        var existing = await db.BillingLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ClinicId == request.ClinicId && l.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.Amount != amount || existing.BalanceType != request.BalanceType)
            {
                throw new InvalidOperationException("This idempotency key was already used for a different adjustment.");
            }
            return new LedgerResult(existing.Id, existing.BalanceAfter, Duplicate: true);
        }

        if (amount < 0)
        {
            var balance = request.BalanceType == LedgerBalanceType.Wallet ? account.WalletBalance : account.IncludedCreditBalance;
            if (balance + amount < 0 || account.Spendable + amount < 0)
            {
                throw new InvalidOperationException(
                    $"This debit would take the {(request.BalanceType == LedgerBalanceType.Wallet ? "wallet" : "included credit")} or the spendable balance below zero (reserved funds stay covered).");
            }
        }

        var entry = await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.ManualAdjustment, request.BalanceType, amount, key,
            new LedgerContext(request.Source, request.Actor, request.Reason.Trim(), CorrelationId: request.IdempotencyKey), now, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: manual {BalanceType} adjustment of {Amount} {Currency} for clinic {ClinicId} by {Actor}: {Reason}.",
            request.BalanceType, amount, account.Currency, request.ClinicId, request.Actor ?? request.Source, request.Reason);
        return new LedgerResult(entry.Id, entry.BalanceAfter, Duplicate: false);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Shared arithmetic (always called with the account row locked)
    // ------------------------------------------------------------------------------------------------------------

    private static void ApplyQuote(BillingUsageRecord usage, RateQuote quote)
    {
        usage.RateId = quote.RateId;
        usage.RateCardId = quote.RateCardId;
        usage.RateSource = quote.RateSource;
        usage.UnitPrice = quote.UnitPrice;
        usage.UnitProviderCost = quote.UnitProviderCost;
        usage.Unit ??= quote.Unit;
        usage.Amount = BillingStore.RoundMoney(quote.UnitPrice * usage.Quantity);
        usage.ProviderCost = BillingStore.RoundMoney(quote.UnitProviderCost * usage.Quantity);
    }

    /// <summary>Returns a reserved usage's hold to the spendable pool (the hold is pooled on the account).</summary>
    private static void ReleaseHold(BillingAccount account, BillingUsageRecord usage, DateTimeOffset now)
    {
        var reserved = BillingStore.RoundMoney(account.ReservedAmount - usage.ReservedAmount);
        if (reserved < 0)
        {
            // Would mean the cached reserved total and the usage records disagree — stop rather than hide it.
            throw new InvalidOperationException(
                $"Billing account {account.Id} has less reserved ({account.ReservedAmount}) than usage {usage.Id} holds ({usage.ReservedAmount}).");
        }
        account.ReservedAmount = reserved;
        account.UpdatedAt = now;
    }

    /// <summary>Charges a usage: included credit first, then the wallet, one ledger row per balance touched. The
    /// wallet may go below zero only here, when the final amount is higher than what was reserved.</summary>
    private static async Task<decimal> ApplySettlementAsync(ApplicationDbContext db, BillingAccount account, BillingUsageRecord usage, decimal quantity,
        DateTimeOffset now, CancellationToken ct)
    {
        usage.Quantity = quantity;
        usage.Amount = BillingStore.RoundMoney((usage.UnitPrice ?? 0) * quantity);
        usage.ProviderCost = BillingStore.RoundMoney((usage.UnitProviderCost ?? 0) * quantity);
        var amount = usage.Amount.Value;

        var fromCredit = Math.Min(amount, Math.Max(0, account.IncludedCreditBalance));
        var fromWallet = BillingStore.RoundMoney(amount - fromCredit);
        var context = new LedgerContext(usage.Source, usage.Actor, UsageRecordId: usage.Id, CorrelationId: usage.IdempotencyKey);

        if (fromCredit > 0)
        {
            await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.IncludedCreditConsumption, LedgerBalanceType.IncludedCredit,
                -fromCredit, $"usage:{usage.Id}:credit", context, now, ct);
        }
        if (fromWallet > 0)
        {
            await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.UsageDebit, LedgerBalanceType.Wallet,
                -fromWallet, $"usage:{usage.Id}:wallet", context, now, ct);
        }

        usage.CreditAmount = fromCredit;
        usage.WalletAmount = fromWallet;
        usage.ChargeStatus = ChargeStatus.Settled;
        usage.ProviderOutcome = ProviderOutcome.Billable;
        usage.SettledAt = now;
        usage.UpdatedAt = now;
        return amount;
    }

    /// <summary>Usage SculptFlow doesn't charge: recorded with SculptFlow amount 0, the provider cost when some rate
    /// knows it (for reporting only — whoever ProviderBilling names pays it), no hold, no wallet or credit use and no
    /// ledger row. A missing rate is fine here.</summary>
    private async Task<UsageResult> SaveNotChargedAsync(ApplicationDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        BillingUsageRecord usage, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var costQuote = await RateResolver.ResolveAsync(db, new RatingRequest(usage.ClinicId, usage.EventType, usage.CountryCode,
            usage.Operator, usage.Provider, occurredAt, usage.ProviderBilling, ForCharging: false), ct);
        if (costQuote is not null && string.Equals(costQuote.Currency, usage.Currency.Trim(), StringComparison.Ordinal))
        {
            usage.RateId = costQuote.RateId;
            usage.RateCardId = costQuote.RateCardId;
            usage.RateSource = costQuote.RateSource;
            usage.UnitProviderCost = costQuote.UnitProviderCost;
            usage.ProviderCost = BillingStore.RoundMoney(costQuote.UnitProviderCost * usage.Quantity);
            usage.Unit ??= costQuote.Unit;
        }
        usage.ChargeStatus = ChargeStatus.NotCharged;
        usage.Amount = 0;
        db.BillingUsageRecords.Add(usage);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogDebug("Billing: recorded uncharged usage {IdempotencyKey} ({EventType}, paid by {ProviderBilling}) for clinic {ClinicId}.",
            usage.IdempotencyKey, usage.EventType, usage.ProviderBilling, usage.ClinicId);
        return ToResult(usage, duplicate: false);
    }

    /// <summary>A refused attempt is kept for the record under a suffixed key, so the event's own key stays free and a
    /// later attempt (after a top-up, or once a rate exists) can still succeed.</summary>
    private async Task<UsageResult> SaveFailedAsync(ApplicationDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        BillingUsageRecord usage, string reason, CancellationToken ct)
    {
        usage.IdempotencyKey = $"{usage.IdempotencyKey}|failed|{Guid.NewGuid():N}";
        usage.ChargeStatus = ChargeStatus.Failed;
        usage.FailureReason = reason;
        usage.ReservedAmount = 0;
        db.BillingUsageRecords.Add(usage);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new UsageResult(UsageOutcome.Failed, usage.Id, usage.Amount, reason, false);
    }

    private static UsageResult ToResult(BillingUsageRecord usage, bool duplicate) => usage.ChargeStatus switch
    {
        ChargeStatus.Reserved => new UsageResult(UsageOutcome.Reserved, usage.Id, usage.Amount, null, duplicate),
        ChargeStatus.Settled => new UsageResult(UsageOutcome.Settled, usage.Id, usage.Amount, null, duplicate),
        ChargeStatus.Released => new UsageResult(UsageOutcome.Released, usage.Id, usage.Amount, null, duplicate),
        ChargeStatus.NotCharged => new UsageResult(UsageOutcome.Recorded, usage.Id, usage.Amount, null, duplicate),
        _ => new UsageResult(UsageOutcome.Failed, usage.Id, usage.Amount, usage.FailureReason, duplicate)
    };

    private static void Validate(BillableEvent e)
    {
        if (e.ClinicId == Guid.Empty) throw new ArgumentException("ClinicId is required.", nameof(e));
        ValidateKey(e.IdempotencyKey);
        if (!BillableEventTypes.IsValid(e.EventType))
        {
            throw new ArgumentException($"'{e.EventType}' isn't a valid billable event type (lowercase letters, digits and _).", nameof(e));
        }
        if (string.IsNullOrWhiteSpace(e.Channel)) throw new ArgumentException("Channel is required.", nameof(e));
        if (e.Quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(e));
        if (!ProviderBillingResponsibility.IsValid(e.ProviderBilling))
        {
            throw new ArgumentException($"'{e.ProviderBilling}' isn't a provider billing responsibility.", nameof(e));
        }
    }

    private static void ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("An idempotency key is required.");
        if (key.Length > MaxKeyLength) throw new ArgumentException($"Idempotency keys are at most {MaxKeyLength} characters.");
    }
}
