using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Engines.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Business.Services.Billing;

public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingUnitOfWorkFactory _units;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly IConfigManager _config;
    private readonly ICacheManager _cache;

    public SubscriptionService(IBillingUnitOfWorkFactory units, TimeProvider time, ILogger<SubscriptionService> logger, IConfigManager config, ICacheManager cache)
    {
        _config = config;
        _cache = cache;
        _units = units;
        _time = time;
        _logger = logger;
    }

    public async Task<ClientSubscription?> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        return await unit.Subscriptions.GetWithPlanReadOnlyAsync(clientId, ct);
    }

    public async Task<ClientSubscription> StartAsync(StartSubscriptionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > BillingService.MaxKeyLength)
        {
            throw new ArgumentException($"An idempotency key of at most {BillingService.MaxKeyLength} characters is required.", nameof(request));
        }
        var now = _time.GetUtcNow();
        var planCode = (request.PlanCode ?? string.Empty).Trim().ToLowerInvariant();
        var chargeKey = $"subscription-start:{request.IdempotencyKey}:charge";

        await using var unit = _units.Create();
        await using var tx = await unit.BeginTransactionAsync(ct);
        var account = await unit.Accounts.LockAsync(request.ClientId, _config.BillingCurrency, ct);

        var subscription = await unit.Subscriptions.GetWithPlanAsync(request.ClientId, ct);
        if (await unit.Ledger.KeyExistsAsync(request.ClientId, chargeKey, ct))
        {
            _logger.LogInformation("Billing: duplicate subscription start {Key} for client {ClientId} ignored.", request.IdempotencyKey, request.ClientId);
            return subscription!;
        }

        var plan = await unit.Plans.GetAsync(planCode, ct);
        if (plan is null || !plan.IsActive)
        {
            throw new ArgumentException($"Plan '{planCode}' doesn't exist or isn't active.", nameof(request));
        }
        if (!string.Equals(plan.Currency.Trim(), account.Currency.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException($"Plan '{planCode}' is priced in {plan.Currency}, but the client's account is in {account.Currency}.", nameof(request));
        }

        var price = request.ChargeFirstPeriod ? plan.Price : 0m;
        // Add-ons are charged with the plan from each period start (none for a free first period).
        var addonsPrice = request.ChargeFirstPeriod ? PlanPeriod.AddonsTotal(await unit.Addons.ListForClientReadOnlyAsync(request.ClientId, ct)) : 0m;
        if (!CanPay(account, price + addonsPrice, plan.IncludedUsageCredit))
        {
            _logger.LogWarning("Billing: can't start plan {Plan} for client {ClientId}: price {Price} {Currency}, wallet {Wallet}, spendable {Spendable}.",
                plan.Code, request.ClientId, price, account.Currency, account.WalletBalance, account.Spendable);
            throw new BillingDeniedException(UsageFailureReason.InsufficientFunds,
                $"The wallet doesn't cover the {plan.Name} plan price ({price:0.00} {account.Currency}). Top up first.");
        }

        var previousPlanId = subscription?.PlanId;
        var isNew = subscription is null;
        if (subscription is null)
        {
            subscription = new ClientSubscription { Id = Guid.NewGuid(), ClientId = request.ClientId, CreatedAt = now };
            unit.Subscriptions.Add(subscription);
        }
        subscription.PlanId = plan.Id;
        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = now;
        subscription.CurrentPeriodEnd = PlanPeriod.End(plan, now);
        subscription.CancelAtPeriodEnd = false;
        subscription.PastDueSince = null;
        subscription.EndedAt = null;
        subscription.UpdatedAt = now;

        var context = new LedgerContext(request.Source, request.Actor, request.Reason ?? (isNew ? "Subscription started" : "Plan changed"),
            SubscriptionId: subscription.Id, PlanId: plan.Id, CorrelationId: request.IdempotencyKey);
        await StartPeriodAsync(unit, account, plan, price, addonsPrice, $"subscription-start:{request.IdempotencyKey}", context, now, ct);

        BillingLedger.LogEvent(unit, request.ClientId, isNew ? BillingEventTypes.SubscriptionStarted : BillingEventTypes.SubscriptionPlanChanged,
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

        await unit.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(request.ClientId), ct);

        _logger.LogInformation("Billing: client {ClientId} {Action} plan {Plan} until {PeriodEnd} (charged {Price} {Currency}, credit {Credit}) by {Actor}.",
            request.ClientId, isNew ? "started" : "changed to", plan.Code, subscription.CurrentPeriodEnd, price, account.Currency,
            plan.IncludedUsageCredit, request.Actor ?? request.Source);
        subscription.Plan = plan;
        return subscription;
    }

    public async Task<RenewalOutcome> RenewIfDueAsync(Guid clientId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();

        await using var unit = _units.Create();
        await using var tx = await unit.BeginTransactionAsync(ct);
        var account = await unit.Accounts.LockAsync(clientId, _config.BillingCurrency, ct);

        var subscription = await unit.Subscriptions.GetWithPlanAsync(clientId, ct);
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

        if (plan.TrialDays is not null)
        {
            // A trial never renews: it ends, and the admin moves the client to a paid plan.
            await ExpireCreditAsync(unit, account, $"subscription:{subscription.Id}:trial-ended:{Stamp(periodEnd)}:credit-expiry",
                new LedgerContext(BillingSource.Worker, Reason: "Trial ended", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
            subscription.Status = SubscriptionStatus.Expired;
            subscription.EndedAt = periodEnd;
            BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionExpired, BillingSource.Worker,
                new { plan = plan.Code, endedAt = periodEnd, trialEnded = true }, now);
            outcome = RenewalOutcome.Expired;
        }
        else if (subscription.CancelAtPeriodEnd)
        {
            await ExpireCreditAsync(unit, account, $"subscription:{subscription.Id}:cancelled-at-end:{Stamp(periodEnd)}:credit-expiry",
                new LedgerContext(BillingSource.Worker, Reason: "Subscription cancelled at period end", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndedAt = periodEnd;
            BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionCancelled, BillingSource.Worker,
                new { plan = plan.Code, endedAt = periodEnd, scheduled = true }, now);
            outcome = RenewalOutcome.Cancelled;
        }
        else
        {
            var newStart = periodEnd;
            var keyBase = $"subscription:{subscription.Id}:period:{Stamp(newStart)}";
            var addonsPrice = PlanPeriod.AddonsTotal(await unit.Addons.ListForClientReadOnlyAsync(clientId, ct));
            if (CanPay(account, plan.Price + addonsPrice, plan.IncludedUsageCredit))
            {
                await StartPeriodAsync(unit, account, plan, plan.Price, addonsPrice, keyBase,
                    new LedgerContext(BillingSource.Worker, Reason: "Subscription renewal", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
                subscription.CurrentPeriodStart = newStart;
                subscription.CurrentPeriodEnd = PlanPeriod.End(plan, newStart);
                subscription.Status = SubscriptionStatus.Active;
                subscription.PastDueSince = null;
                BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionRenewed, BillingSource.Worker,
                    new { plan = plan.Code, charged = plan.Price, addons = addonsPrice, currency = account.Currency, periodEnd = subscription.CurrentPeriodEnd }, now);
                outcome = RenewalOutcome.Renewed;
            }
            else if (subscription.Status == SubscriptionStatus.Active)
            {
                // The period is over either way, so its unused credit goes; the plan stays usable during the grace period.
                await ExpireCreditAsync(unit, account, $"subscription:{subscription.Id}:ended:{Stamp(periodEnd)}:credit-expiry",
                    new LedgerContext(BillingSource.Worker, Reason: "Period ended unpaid", SubscriptionId: subscription.Id, PlanId: plan.Id), now, ct);
                subscription.Status = SubscriptionStatus.PastDue;
                subscription.PastDueSince = now;
                BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionPastDue, BillingSource.Worker,
                    new { plan = plan.Code, due = plan.Price + addonsPrice, currency = account.Currency, wallet = account.WalletBalance, graceDays = _config.BillingGracePeriodDays }, now);
                outcome = RenewalOutcome.PastDue;
            }
            else if (now >= (subscription.PastDueSince ?? now).AddDays(_config.BillingGracePeriodDays))
            {
                subscription.Status = SubscriptionStatus.Expired;
                subscription.EndedAt = now;
                BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionExpired, BillingSource.Worker,
                    new { plan = plan.Code, pastDueSince = subscription.PastDueSince }, now);
                outcome = RenewalOutcome.Expired;
            }
            else
            {
                return RenewalOutcome.PastDue; // still in grace, still unpaid: nothing changes
            }
        }

        subscription.UpdatedAt = now;
        await unit.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(clientId), ct);

        if (outcome == RenewalOutcome.Renewed)
        {
            _logger.LogInformation("Billing: renewed plan {Plan} for client {ClientId} until {PeriodEnd} ({Price} {Currency}).",
                plan.Code, clientId, subscription.CurrentPeriodEnd, plan.Price, account.Currency);
        }
        else
        {
            _logger.LogWarning("Billing: subscription of client {ClientId} (plan {Plan}) is now {Status}.", clientId, plan.Code, subscription.Status);
        }
        return outcome;
    }

    public async Task<ClientSubscription?> CancelAsync(Guid clientId, bool immediately, string source, string? actor, string? reason, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();

        await using var unit = _units.Create();
        await using var tx = await unit.BeginTransactionAsync(ct);
        var account = await unit.Accounts.LockAsync(clientId, _config.BillingCurrency, ct);

        var subscription = await unit.Subscriptions.GetWithPlanAsync(clientId, ct);
        if (subscription is null) return null;
        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired) return subscription;

        if (immediately)
        {
            await ExpireCreditAsync(unit, account, $"subscription:{subscription.Id}:cancelled:{Stamp(subscription.CurrentPeriodStart)}:credit-expiry",
                new LedgerContext(source, actor, reason ?? "Subscription cancelled", SubscriptionId: subscription.Id, PlanId: subscription.PlanId), now, ct);
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndedAt = now;
            BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionCancelled, source,
                new { plan = subscription.Plan?.Code, immediately = true, actor, reason }, now);
        }
        else if (!subscription.CancelAtPeriodEnd)
        {
            subscription.CancelAtPeriodEnd = true;
            BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionCancelScheduled, source,
                new { plan = subscription.Plan?.Code, endsAt = subscription.CurrentPeriodEnd, actor, reason }, now);
        }
        subscription.UpdatedAt = now;

        await unit.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(clientId), ct);
        _logger.LogInformation("Billing: subscription of client {ClientId} cancelled ({When}) by {Actor}.",
            clientId, immediately ? "now" : "at period end", actor ?? source);
        return subscription;
    }

    public async Task<ClientSubscription?> ResumeAsync(Guid clientId, string source, string? actor, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        await using var unit = _units.Create();
        await using var tx = await unit.BeginTransactionAsync(ct);
        await unit.Accounts.LockAsync(clientId, _config.BillingCurrency, ct);

        var subscription = await unit.Subscriptions.GetWithPlanAsync(clientId, ct);
        if (subscription is null || !subscription.CancelAtPeriodEnd
            || subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
        {
            return subscription;
        }
        subscription.CancelAtPeriodEnd = false;
        subscription.UpdatedAt = now;
        BillingLedger.LogEvent(unit, clientId, BillingEventTypes.SubscriptionResumed, source, new { plan = subscription.Plan?.Code, actor }, now);
        await unit.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(clientId), ct);
        return subscription;
    }

    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        List<Guid> clientIds;
        await using (var unit = _units.Create())
        {
            clientIds = (await unit.Subscriptions.ListDueClientIdsAsync(now, ct)).ToList();
        }

        var changed = 0;
        foreach (var clientId in clientIds)
        {
            // One transaction per client: a failure for one client never blocks or rolls back another.
            try
            {
                var outcome = await RenewIfDueAsync(clientId, ct);
                if (outcome is not (RenewalOutcome.NotDue or RenewalOutcome.PastDue)) changed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Billing: renewal processing failed for client {ClientId}; it will be retried on the next run.", clientId);
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
    /// operation's idempotency key), and the client's add-ons are charged in one addon_charge row when they cost anything.</summary>
    private static async Task StartPeriodAsync(IBillingUnitOfWork unit, BillingAccount account, SubscriptionPlan plan, decimal price,
        decimal addonsPrice, string keyBase, LedgerContext context, DateTimeOffset now, CancellationToken ct)
    {
        await ExpireCreditAsync(unit, account, $"{keyBase}:credit-expiry", context, now, ct);
        if (plan.IncludedUsageCredit > 0)
        {
            await BillingLedger.PostAsync(unit, account, LedgerEntryType.IncludedCreditGrant, LedgerBalanceType.IncludedCredit,
                plan.IncludedUsageCredit, $"{keyBase}:credit-grant", context, now, ct);
        }
        await BillingLedger.PostAsync(unit, account, LedgerEntryType.SubscriptionCharge, LedgerBalanceType.Wallet,
            -price, $"{keyBase}:charge", context, now, ct);
        if (addonsPrice > 0)
        {
            await BillingLedger.PostAsync(unit, account, LedgerEntryType.AddonCharge, LedgerBalanceType.Wallet,
                -addonsPrice, $"{keyBase}:addons", context with { Reason = "Add-ons" }, now, ct);
        }
    }

    private static async Task ExpireCreditAsync(IBillingUnitOfWork unit, BillingAccount account, string key, LedgerContext context, DateTimeOffset now,
        CancellationToken ct)
    {
        if (account.IncludedCreditBalance > 0)
        {
            await BillingLedger.PostAsync(unit, account, LedgerEntryType.IncludedCreditExpiry, LedgerBalanceType.IncludedCredit,
                -account.IncludedCreditBalance, key, context, now, ct);
        }
    }

    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
}
