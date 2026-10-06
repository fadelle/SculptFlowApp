using System.Data;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

/// <summary>
/// Gives every money operation its OWN DbContext, so a billing transaction never flushes (or is rolled back with)
/// unrelated tracked changes of the request that called it — e.g. MessageService's half-built message row. Scoped,
/// built from the app's DbContextOptions; tests pass their own options.
/// </summary>
public sealed class BillingDbFactory
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public BillingDbFactory(DbContextOptions<ApplicationDbContext> options)
    {
        _options = options;
    }

    public ApplicationDbContext Create() => new(_options);
}

/// <summary>The shared transactional building blocks of the billing module. Every operation that changes money:
///   1. opens a READ COMMITTED transaction on its own context,
///   2. locks the clinic's billing.accounts row (<see cref="LockAccountAsync"/>) — this serializes all money
///      operations of one clinic, so two workers can never spend the same balance,
///   3. checks idempotency (existing usage record / ledger key) AFTER taking the lock,
///   4. changes balances only through <see cref="PostLedgerAsync"/>, which writes the matching ledger row,
///   5. commits once at the end. Any exception before the commit rolls everything back.</summary>
internal static class BillingStore
{
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    public static Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(ApplicationDbContext db, CancellationToken ct) =>
        db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

    /// <summary>Creates the clinic's account if it doesn't exist yet (race-safe), then locks it FOR UPDATE until the
    /// transaction ends. Must run inside a transaction.</summary>
    public static async Task<BillingAccount> LockAccountAsync(ApplicationDbContext db, Guid clinicId, string currency, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"insert into billing.accounts (id, clinic_id, currency) values ({Guid.NewGuid()}, {clinicId}, {currency}) on conflict (clinic_id) do nothing",
            ct);
        var rows = await db.BillingAccounts
            .FromSqlInterpolated($"select * from billing.accounts where clinic_id = {clinicId} for update")
            .ToListAsync(ct);
        return rows.Single();
    }

    /// <summary>Applies one signed movement to one balance of the (locked) account and writes its ledger row. Saves
    /// (inside the caller's transaction) right away so rows get their posting sequence (seq) in posting order — EF
    /// would otherwise reorder one batch of inserts by key. balance_after = the balance right after this movement.</summary>
    public static async Task<BillingLedgerEntry> PostLedgerAsync(ApplicationDbContext db, BillingAccount account, string entryType,
        string balanceType, decimal amount, string idempotencyKey, LedgerContext context, DateTimeOffset now, CancellationToken ct)
    {
        amount = RoundMoney(amount);
        if (balanceType == LedgerBalanceType.Wallet)
        {
            account.WalletBalance = RoundMoney(account.WalletBalance + amount);
        }
        else
        {
            account.IncludedCreditBalance = RoundMoney(account.IncludedCreditBalance + amount);
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
            Actor = Truncate(context.Actor, 200),
            Reason = context.Reason,
            Reference = Truncate(context.Reference, 200),
            CorrelationId = Truncate(context.CorrelationId, 200),
            CreatedAt = now
        };
        db.BillingLedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public static Task<bool> LedgerKeyExistsAsync(ApplicationDbContext db, Guid clinicId, string key, CancellationToken ct) =>
        db.BillingLedgerEntries.AnyAsync(e => e.ClinicId == clinicId && e.IdempotencyKey == key, ct);

    /// <summary>Writes a subscription/billing audit row to the existing events table, in the same transaction.</summary>
    public static void LogEvent(ApplicationDbContext db, Guid clinicId, string eventType, string source, object metadata, DateTimeOffset now)
    {
        db.Events.Add(new EventLog
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            EventType = eventType,
            Source = source,
            Metadata = System.Text.Json.JsonSerializer.Serialize(metadata),
            CreatedAt = now
        });
    }

    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Who/why for a ledger entry (auditing: source, actor, reason, correlation, related entity).</summary>
internal sealed record LedgerContext(
    string Source,
    string? Actor = null,
    string? Reason = null,
    Guid? UsageRecordId = null,
    Guid? SubscriptionId = null,
    Guid? PlanId = null,
    string? Reference = null,
    string? CorrelationId = null);

/// <summary>event_type values the billing module writes to the events table.</summary>
public static class BillingEventTypes
{
    public const string SubscriptionStarted = "subscription_started";
    public const string SubscriptionPlanChanged = "subscription_plan_changed";
    public const string SubscriptionRenewed = "subscription_renewed";
    public const string SubscriptionPastDue = "subscription_past_due";
    public const string SubscriptionExpired = "subscription_expired";
    public const string SubscriptionCancelled = "subscription_cancelled";
    public const string SubscriptionCancelScheduled = "subscription_cancel_scheduled";
    public const string SubscriptionResumed = "subscription_resumed";
    public const string ProviderBillingChanged = "provider_billing_changed";
}
