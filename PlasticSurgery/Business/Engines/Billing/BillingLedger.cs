using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Business.Engines.Billing;

/// <summary>The only way balances change: one signed movement of one balance, always with its ledger row, inside the
/// caller's billing transaction (see IBillingUnitOfWork for the full rules).</summary>
public static class BillingLedger
{
    /// <summary>Applies one signed movement to one balance of the (locked) account and writes its ledger row. Saves
    /// (inside the caller's transaction) right away so rows get their posting sequence (seq) in posting order — EF
    /// would otherwise reorder one batch of inserts by key. balance_after = the balance right after this movement.</summary>
    internal static async Task<BillingLedgerEntry> PostAsync(IBillingUnitOfWork unit, BillingAccount account, string entryType,
        string balanceType, decimal amount, string idempotencyKey, LedgerContext context, DateTimeOffset now, CancellationToken ct)
    {
        amount = BillingMath.RoundMoney(amount);
        if (balanceType == LedgerBalanceType.Wallet)
        {
            account.WalletBalance = BillingMath.RoundMoney(account.WalletBalance + amount);
        }
        else
        {
            account.IncludedCreditBalance = BillingMath.RoundMoney(account.IncludedCreditBalance + amount);
            if (account.IncludedCreditBalance < 0)
            {
                throw new InvalidOperationException("Included credit can't go below zero.");
            }
        }
        account.UpdatedAt = now;

        var entry = new BillingLedgerEntry
        {
            Id = Guid.NewGuid(),
            ClinicId = account.ClinicId,
            BillingAccountId = account.Id,
            EntryType = entryType,
            BalanceType = balanceType,
            Amount = amount,
            BalanceAfter = balanceType == LedgerBalanceType.Wallet ? account.WalletBalance : account.IncludedCreditBalance,
            Currency = account.Currency,
            IdempotencyKey = idempotencyKey,
            UsageRecordId = context.UsageRecordId,
            SubscriptionId = context.SubscriptionId,
            PlanId = context.PlanId,
            Source = context.Source,
            Actor = BillingMath.Truncate(context.Actor, 200),
            Reason = context.Reason,
            Reference = BillingMath.Truncate(context.Reference, 200),
            CorrelationId = BillingMath.Truncate(context.CorrelationId, 200),
            CreatedAt = now
        };
        unit.Ledger.Add(entry);
        await unit.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>Queues a subscription/billing audit row in the events table, written with the operation's commit.</summary>
    public static void LogEvent(IBillingUnitOfWork unit, Guid clinicId, string eventType, string source, object metadata, DateTimeOffset now)
    {
        unit.Events.Add(new EventLog
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            EventType = eventType,
            Source = source,
            Metadata = System.Text.Json.JsonSerializer.Serialize(metadata),
            CreatedAt = now
        });
    }
}
