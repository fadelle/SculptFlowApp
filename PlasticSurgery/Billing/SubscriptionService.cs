using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

/// <param name="IdempotencyKey">Makes a retried request (double click, network retry) a no-op. Max 150 chars.</param>
/// <param name="ChargeFirstPeriod">false = the first period is free (trial, or paid outside the wallet).</param>
public sealed record StartSubscriptionRequest(
    Guid ClinicId,
    string PlanCode,
    string IdempotencyKey,
    bool ChargeFirstPeriod,
    string Source,
    string? Actor = null,
    string? Reason = null);

public enum RenewalOutcome
{
    /// <summary>Nothing was due.</summary>
    NotDue,
    Renewed,
    /// <summary>The renewal couldn't be paid; the subscription is (still) past due, within its grace period.</summary>
    PastDue,
    Expired,
    Cancelled
}

/// <summary>
/// Subscription lifecycle, kept deliberately small:
///   start/change plan  -> active, new period from now, plan price charged from the wallet (unless waived),
///                         remaining included credit expired, the plan's credit granted
///   renewal (due)      -> period advances, price charged, credit reset; can't pay -> past_due (grace period)
///   past_due + grace   -> expired       cancel -> cancelled now, or at period end
/// No proration, no invoices, no payment gateway: the wallet is the payment method (top-ups are admin actions).
/// Every money movement goes through the ledger in the same transaction as the status change, under the clinic's
/// billing account lock; ledger keys derived from the period make a repeated run a no-op.
/// </summary>
public interface ISubscriptionService
{
    Task<ClinicSubscription?> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Starts a subscription or switches the clinic to another plan (a new period starts now). Throws
    /// BillingDeniedException when the wallet can't pay the price, ArgumentException for an unknown/inactive plan.</summary>
    Task<ClinicSubscription> StartAsync(StartSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Renews (or moves to past_due / expired / cancelled) when the clinic's period has ended.</summary>
    Task<RenewalOutcome> RenewIfDueAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Immediately = ends now (no refund); otherwise it ends at the period end instead of renewing.</summary>
    Task<ClinicSubscription?> CancelAsync(Guid clinicId, bool immediately, string source, string? actor, string? reason, CancellationToken ct = default);

    /// <summary>Undoes a scheduled cancellation.</summary>
    Task<ClinicSubscription?> ResumeAsync(Guid clinicId, string source, string? actor, CancellationToken ct = default);

    /// <summary>Worker entry point: every subscription whose period ended or that is past due.</summary>
    Task<int> ProcessDueAsync(CancellationToken ct = default);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly BillingDbFactory _dbFactory;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(BillingDbFactory dbFactory, IOptions<BillingOptions> options, TimeProvider time, ILogger<SubscriptionService> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<ClinicSubscription?> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        return await db.ClinicSubscriptions.AsNoTracking().Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
    }

    public async Task<ClinicSubscription> StartAsync(StartSubscriptionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > BillingService.MaxKeyLength)
        {
            throw new ArgumentException($"An idempotency key of at most {BillingService.MaxKeyLength} characters is required.", nameof(request));
        }
        var now = _time.GetUtcNow();
        var planCode = (request.PlanCode ?? string.Empty).Trim().ToLowerInvariant();
        var chargeKey = $"subscription-start:{request.IdempotencyKey}:charge";

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, request.ClinicId, _options.NormalizedCurrency, ct);

        var subscription = await db.ClinicSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == request.ClinicId, ct);
        if (await BillingStore.LedgerKeyExistsAsync(db, request.ClinicId, chargeKey, ct))
        {
            _logger.LogInformation("Billing: duplicate subscription start {Key} for clinic {ClinicId} ignored.", request.IdempotencyKey, request.ClinicId);
            return subscription!;
        }

        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == planCode, ct);
        if (plan is null || !plan.IsActive)
        {
            throw new ArgumentException($"Plan '{planCode}' doesn't exist or isn't active.", nameof(request));
        }
        if (!string.Equals(plan.Currency.Trim(), account.Currency.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException($"Plan '{planCode}' is priced in {plan.Currency}, but the clinic's account is in {account.Currency}.", nameof(request));
        }

        var price = request.ChargeFirstPeriod ? plan.Price : 0m;
        if (!CanPay(account, price, plan.IncludedUsageCredit))
        {
            _logger.LogWarning("Billing: can't start plan {Plan} for clinic {ClinicId}: price {Price} {Currency}, wallet {Wallet}, spendable {Spendable}.",
                plan.Code, request.ClinicId, price, account.Currency, account.WalletBalance, account.Spendable);
            throw new BillingDeniedException(UsageFailureReason.InsufficientFunds,
                $"The wallet doesn't cover the {plan.Name} plan price ({price:0.00} {account.Currency}). Top up first.");
        }

        var previousPlanId = subscription?.PlanId;
        var isNew = subscription is null;
        if (subscription is null)
        {
            subscription = new ClinicSubscription { Id = Guid.NewGuid(), ClinicId = request.ClinicId, CreatedAt = now };
            db.ClinicSubscriptions.Add(subscription);
        }
        subscription.PlanId = plan.Id;
        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = now;
        subscription.CurrentPeriodEnd = BillingPeriod.Advance(now, plan.BillingPeriod);
        subscription.CancelAtPeriodEnd = false;
        subscription.PastDueSince = null;
        subscription.EndedAt = null;
        subscription.UpdatedAt = now;

        var context = new LedgerContext(request.Source, request.Actor, request.Reason ?? (isNew ? "Subscription started" : "Plan changed"),
            SubscriptionId: subscription.Id, PlanId: plan.Id, CorrelationId: request.IdempotencyKey);
        await StartPeriodAsync(db, account, plan, price, $"subscription-start:{request.IdempotencyKey}", context, now, ct);

        BillingStore.LogEvent(db, request.ClinicId, isNew ? BillingEventTypes.SubscriptionStarted : BillingEventTypes.SubscriptionPlanChanged,
            request.Source, new
            {
                plan = plan.Code,
                previousPlanId,
                charged = price,
                currency = account.Currency,
                periodEnd = subscription.CurrentPeriodEnd,
                actor = request.Actor,
                reason = request.Reason,
                idempotencyKey = request.IdempotencyKey
            }, now);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: clinic {ClinicId} {Action} plan {Plan} until {PeriodEnd} (charged {Price} {Currency}, credit {Credit}) by {Actor}.",
            request.ClinicId, isNew ? "started" : "changed to", plan.Code, subscription.CurrentPeriodEnd, price, account.Currency,
            plan.IncludedUsageCredit, request.Actor ?? request.Source);
        subscription.Plan = plan;
        return subscription;
    }

    public async Task<RenewalOutcome> RenewIfDueAsync(Guid clinicId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, clinicId, _options.NormalizedCurrency, ct);

        var subscription = await db.ClinicSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        if (subscription is null || subscription.Status is SubscriptionStatus.Expired or SubscriptionStatus.Cancelled)
        {
            return RenewalOutcome.NotDue;
        }
        if (subscription.Status == SubscriptionStatus.Active && now < subscription.CurrentPeriodEnd)
        {
            return RenewalOutcome.NotDue;
        }

        var plan = subscription.Plan!;
        var periodEnd = subscription.CurrentPeriodEnd;
        RenewalOutcome outcome;

        if (subscription.CancelAtPeriodEnd)
        {
            await ExpireCreditAsync(db, account, $"subscription:{subscription.Id}:cancelled-at-end:{Stamp(periodEnd)}:credit-expiry",
                new LedgerContext(BillingSource.Worker, Reason: "Subscription cancelled at period end", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndedAt = periodEnd;
            BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionCancelled, BillingSource.Worker,
                new { plan = plan.Code, endedAt = periodEnd, scheduled = true }, now);
            outcome = RenewalOutcome.Cancelled;
        }
        else
        {
            var newStart = periodEnd;
            var keyBase = $"subscription:{subscription.Id}:period:{Stamp(newStart)}";
            if (CanPay(account, plan.Price, plan.IncludedUsageCredit))
            {
                await StartPeriodAsync(db, account, plan, plan.Price, keyBase,
                    new LedgerContext(BillingSource.Worker, Reason: "Subscription renewal", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
                subscription.CurrentPeriodStart = newStart;
                subscription.CurrentPeriodEnd = BillingPeriod.Advance(newStart, plan.BillingPeriod);
                subscription.Status = SubscriptionStatus.Active;
                subscription.PastDueSince = null;
                BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionRenewed, BillingSource.Worker,
                    new { plan = plan.Code, charged = plan.Price, currency = account.Currency, periodEnd = subscription.CurrentPeriodEnd }, now);
                outcome = RenewalOutcome.Renewed;
            }
            else if (subscription.Status == SubscriptionStatus.Active)
            {
                // The period is over either way, so its unused credit goes; the plan stays usable during the grace period.
                await ExpireCreditAsync(db, account, $"subscription:{subscription.Id}:ended:{Stamp(periodEnd)}:credit-expiry",
                    new LedgerContext(BillingSource.Worker, Reason: "Period ended unpaid", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
                subscription.Status = SubscriptionStatus.PastDue;
                subscription.PastDueSince = now;
                BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionPastDue, BillingSource.Worker,
                    new { plan = plan.Code, due = plan.Price, currency = account.Currency, wallet = account.WalletBalance, graceDays = _options.GracePeriodDays }, now);
                outcome = RenewalOutcome.PastDue;
            }
            else if (now >= (subscription.PastDueSince ?? now).AddDays(Math.Max(0, _options.GracePeriodDays)))
            {
                subscription.Status = SubscriptionStatus.Expired;
                subscription.EndedAt = now;
                BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionExpired, BillingSource.Worker,
                    new { plan = plan.Code, pastDueSince = subscription.PastDueSince }, now);
                outcome = RenewalOutcome.Expired;
            }
            else
            {
                return RenewalOutcome.PastDue; // still in grace, still unpaid: nothing changes
            }
        }

        subscription.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (outcome == RenewalOutcome.Renewed)
        {
            _logger.LogInformation("Billing: renewed plan {Plan} for clinic {ClinicId} until {PeriodEnd} ({Price} {Currency}).",
                plan.Code, clinicId, subscription.CurrentPeriodEnd, plan.Price, account.Currency);
        }
        else
        {
            _logger.LogWarning("Billing: subscription of clinic {ClinicId} (plan {Plan}) is now {Status}.", clinicId, plan.Code, subscription.Status);
        }
        return outcome;
    }

    public async Task<ClinicSubscription?> CancelAsync(Guid clinicId, bool immediately, string source, string? actor, string? reason, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var account = await BillingStore.LockAccountAsync(db, clinicId, _options.NormalizedCurrency, ct);

        var subscription = await db.ClinicSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        if (subscription is null) return null;
        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired) return subscription;

        if (immediately)
        {
            await ExpireCreditAsync(db, account, $"subscription:{subscription.Id}:cancelled:{Stamp(subscription.CurrentPeriodStart)}:credit-expiry",
                new LedgerContext(source, actor, reason ?? "Subscription cancelled", SubscriptionId: subscription.Id, PlanId: subscription.PlanId), now, ct);
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndedAt = now;
            BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionCancelled, source,
                new { plan = subscription.Plan?.Code, immediately = true, actor, reason }, now);
        }
        else if (!subscription.CancelAtPeriodEnd)
        {
            subscription.CancelAtPeriodEnd = true;
            BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionCancelScheduled, source,
                new { plan = subscription.Plan?.Code, endsAt = subscription.CurrentPeriodEnd, actor, reason }, now);
        }
        subscription.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("Billing: subscription of clinic {ClinicId} cancelled ({When}) by {Actor}.",
            clinicId, immediately ? "now" : "at period end", actor ?? source);
        return subscription;
    }

    public async Task<ClinicSubscription?> ResumeAsync(Guid clinicId, string source, string? actor, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        await BillingStore.LockAccountAsync(db, clinicId, _options.NormalizedCurrency, ct);

        var subscription = await db.ClinicSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        if (subscription is null || !subscription.CancelAtPeriodEnd
            || subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            return subscription;
        }
        subscription.CancelAtPeriodEnd = false;
        subscription.UpdatedAt = now;
        BillingStore.LogEvent(db, clinicId, BillingEventTypes.SubscriptionResumed, source, new { plan = subscription.Plan?.Code, actor }, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return subscription;
    }

    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        List<Guid> clinicIds;
        await using (var db = _dbFactory.Create())
        {
            clinicIds = await db.ClinicSubscriptions.AsNoTracking()
                .Where(s => (s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= now) || s.Status == SubscriptionStatus.PastDue)
                .Select(s => s.ClinicId)
                .ToListAsync(ct);
        }

        var changed = 0;
        foreach (var clinicId in clinicIds)
        {
            // One transaction per clinic: a failure for one clinic never blocks or rolls back another.
            try
            {
                var outcome = await RenewIfDueAsync(clinicId, ct);
                if (outcome is not (RenewalOutcome.NotDue or RenewalOutcome.PastDue)) changed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Billing: renewal processing failed for clinic {ClinicId}; it will be retried on the next run.", clinicId);
            }
        }
        return changed;
    }

    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Can the wallet pay <paramref name="price"/> while keeping every existing reservation covered once the
    /// old credit is replaced by <paramref name="newCredit"/>? Subscription fees are paid from wallet money only.</summary>
    private static bool CanPay(BillingAccount account, decimal price, decimal newCredit)
    {
        if (price <= 0) return true;
        var spendableAfter = account.WalletBalance - price + newCredit - account.ReservedAmount;
        return account.WalletBalance >= price && spendableAfter >= 0;
    }

    /// <summary>Starts a paid period: old credit expires, the plan's credit is granted, the price is charged (a 0
    /// charge row is still written so every period start has exactly one charge row, and it carries the
    /// operation's idempotency key).</summary>
    private static async Task StartPeriodAsync(ApplicationDbContext db, BillingAccount account, SubscriptionPlan plan, decimal price,
        string keyBase, LedgerContext context, DateTimeOffset now, CancellationToken ct)
    {
        await ExpireCreditAsync(db, account, $"{keyBase}:credit-expiry", context, now, ct);
        if (plan.IncludedUsageCredit > 0)
        {
            await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.IncludedCreditGrant, LedgerBalanceType.IncludedCredit,
                plan.IncludedUsageCredit, $"{keyBase}:credit-grant", context, now, ct);
        }
        await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.SubscriptionCharge, LedgerBalanceType.Wallet,
            -price, $"{keyBase}:charge", context, now, ct);
    }

    private static async Task ExpireCreditAsync(ApplicationDbContext db, BillingAccount account, string key, LedgerContext context, DateTimeOffset now,
        CancellationToken ct)
    {
        if (account.IncludedCreditBalance > 0)
        {
            await BillingStore.PostLedgerAsync(db, account, LedgerEntryType.IncludedCreditExpiry, LedgerBalanceType.IncludedCredit,
                -account.IncludedCreditBalance, key, context, now, ct);
        }
    }

    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
}
