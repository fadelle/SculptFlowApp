using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Billing;

/// <summary>Read side of billing: the clinic's billing view, usage and transaction history, admin overviews,
/// reconciliation (cached balances vs the ledger), price quotes and a revenue/cost/margin report. Nothing here
/// changes data.</summary>
public interface IBillingQueryService
{
    /// <summary>Clinic-safe summary (no provider names, costs or margins).</summary>
    Task<ClinicBillingSummary> GetSummaryAsync(Guid clinicId, CancellationToken ct = default);
    Task<PagedResponse<ClinicUsageRow>> ListClinicUsageAsync(Guid clinicId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResponse<BillingTransactionRow>> ListTransactionsAsync(Guid clinicId, int page, int pageSize, CancellationToken ct = default);

    Task<AdminBillingOverview?> GetAdminOverviewAsync(Guid clinicId, CancellationToken ct = default);
    Task<PagedResponse<AdminUsageRow>> ListAdminUsageAsync(Guid clinicId, string? status, string? eventType, DateTimeOffset? from,
        DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResponse<AdminLedgerRow>> ListAdminLedgerAsync(Guid clinicId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<BillingAccountRow>> ListAccountsAsync(CancellationToken ct = default);
    Task<ReconciliationResponse> ReconcileAsync(Guid clinicId, CancellationToken ct = default);
    /// <summary>What SculptFlow would charge for the event on an account with this provider billing (default:
    /// SculptFlow-funded). Null when nothing prices it.</summary>
    Task<QuoteResponse?> QuoteAsync(Guid clinicId, string eventType, string? countryCode, string? @operator, string? provider,
        DateTimeOffset? at, string? providerBilling = null, CancellationToken ct = default);
    Task<BillingReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public class BillingQueryService : IBillingQueryService
{
    private const int RecentTransactions = 15;

    private readonly BillingDbFactory _dbFactory;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly IProviderBillingService _providerBilling;

    public BillingQueryService(BillingDbFactory dbFactory, IOptions<BillingOptions> options, TimeProvider time, IProviderBillingService providerBilling)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _time = time;
        _providerBilling = providerBilling;
    }

    public async Task<ClinicBillingSummary> GetSummaryAsync(Guid clinicId, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        return await BuildSummaryAsync(db, clinicId, ct);
    }

    public async Task<PagedResponse<ClinicUsageRow>> ListClinicUsageAsync(Guid clinicId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var db = _dbFactory.Create();
        // Failed attempts (refused for balance/price) are kept for support but aren't usage the clinic paid for.
        var query = db.BillingUsageRecords.AsNoTracking().Where(u => u.ClinicId == clinicId && u.ChargeStatus != ChargeStatus.Failed);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(u => u.OccurredAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<ClinicUsageRow>(rows.Select(u =>
        {
            var charged = u.ChargeStatus != ChargeStatus.NotCharged;
            return new ClinicUsageRow(
                u.Id, u.OccurredAt, u.EventType, BillableEventTypes.Label(u.EventType), u.Channel, u.Quantity, u.Unit, u.CountryCode,
                u.UnitPrice, u.Amount, u.CreditAmount, u.WalletAmount, u.RefundedAmount, u.ChargeStatus, u.FailureReason,
                charged, ClinicPaidByLabel(u.ProviderBilling, u.Channel, charged));
        }).ToList(), total, page, pageSize);
    }

    public async Task<PagedResponse<BillingTransactionRow>> ListTransactionsAsync(Guid clinicId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var db = _dbFactory.Create();
        var query = db.BillingLedgerEntries.AsNoTracking().Where(l => l.ClinicId == clinicId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(l => l.Seq)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<BillingTransactionRow>(rows.Select(ToTransaction).ToList(), total, page, pageSize);
    }

    public async Task<AdminBillingOverview?> GetAdminOverviewAsync(Guid clinicId, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return null;

        var summary = await BuildSummaryAsync(db, clinicId, ct);
        var subscription = await db.ClinicSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        var customCard = await db.BillingRateCards.AsNoTracking().Where(c => c.ClinicId == clinicId).Select(c => c.Code).FirstOrDefaultAsync(ct);

        // Everything that counted at the provider this period, charged by SculptFlow or not.
        var usage = await db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.ProviderOutcome == ProviderOutcome.Billable && u.OccurredAt >= summary.UsageSince)
            .GroupBy(u => new { u.EventType, u.Channel, u.ProviderBilling })
            .Select(g => new
            {
                g.Key.EventType, g.Key.Channel, g.Key.ProviderBilling, Count = g.Count(), Quantity = g.Sum(u => u.Quantity),
                Amount = g.Sum(u => u.ChargeStatus == ChargeStatus.Settled ? (u.Amount ?? 0) - u.RefundedAmount : 0),
                Cost = g.Sum(u => u.ProviderCost ?? 0)
            })
            .ToListAsync(ct);
        var openReservations = await db.BillingUsageRecords.CountAsync(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.Reserved, ct);

        return new AdminBillingOverview(
            clinicId, clinic.Name, summary,
            subscription is null ? null : ToSubscription(subscription),
            customCard,
            usage.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Count)
                .Select(x => new AdminUsageBreakdownRow(x.EventType, x.Channel, x.ProviderBilling, x.Count, x.Quantity, x.Amount, x.Cost,
                    x.Amount - (x.ProviderBilling == ProviderBillingResponsibility.PlatformFunded ? x.Cost : 0))).ToList(),
            openReservations,
            await ReconcileCoreAsync(db, clinicId, ct),
            await _providerBilling.ListAccountsAsync(clinicId, ct));
    }

    public async Task<PagedResponse<AdminUsageRow>> ListAdminUsageAsync(Guid clinicId, string? status, string? eventType, DateTimeOffset? from,
        DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var db = _dbFactory.Create();
        var query = db.BillingUsageRecords.AsNoTracking().Where(u => u.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(u => u.ChargeStatus == status);
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(u => u.EventType == eventType);
        if (from is not null) query = query.Where(u => u.OccurredAt >= from);
        if (to is not null) query = query.Where(u => u.OccurredAt < to);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(u => u.OccurredAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<AdminUsageRow>(rows.Select(u => new AdminUsageRow(
            u.Id, u.IdempotencyKey, u.OccurredAt, u.EventType, u.Channel, u.ChannelIntegrationId, u.ProviderBilling, u.Quantity, u.Unit,
            u.CountryCode, u.Operator, u.Provider,
            u.RateSource, u.RateId, u.UnitProviderCost, u.UnitPrice, u.ProviderCost, u.Amount, u.ReservedAmount, u.CreditAmount,
            u.WalletAmount, u.RefundedAmount, u.Currency.Trim(), u.ChargeStatus, u.ProviderOutcome, u.FailureReason, u.ReleaseReason, u.MessageId,
            u.ConversationId, u.CampaignId, u.Source, u.Actor, u.ReservedAt, u.SettledAt, u.ReleasedAt, u.RefundedAt)).ToList(),
            total, page, pageSize);
    }

    public async Task<PagedResponse<AdminLedgerRow>> ListAdminLedgerAsync(Guid clinicId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var db = _dbFactory.Create();
        var query = db.BillingLedgerEntries.AsNoTracking().Where(l => l.ClinicId == clinicId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(l => l.Seq)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<AdminLedgerRow>(rows.Select(l => new AdminLedgerRow(
            l.Id, l.CreatedAt, l.EntryType, l.BalanceType, l.Amount, l.BalanceAfter, l.Currency.Trim(), l.IdempotencyKey,
            l.UsageRecordId, l.SubscriptionId, l.PlanId, l.Source, l.Actor, l.Reason, l.Reference, l.CorrelationId)).ToList(),
            total, page, pageSize);
    }

    public async Task<IReadOnlyList<BillingAccountRow>> ListAccountsAsync(CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var clinics = await db.Clinics.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var accounts = await db.BillingAccounts.AsNoTracking().ToDictionaryAsync(a => a.ClinicId, ct);
        var subscriptions = await db.ClinicSubscriptions.AsNoTracking().Include(s => s.Plan).ToDictionaryAsync(s => s.ClinicId, ct);
        return clinics.Select(c =>
        {
            accounts.TryGetValue(c.Id, out var a);
            subscriptions.TryGetValue(c.Id, out var s);
            return new BillingAccountRow(c.Id, c.Name, s?.Plan?.Code, s?.Status, s?.CurrentPeriodEnd,
                a?.WalletBalance ?? 0, a?.IncludedCreditBalance ?? 0, a?.ReservedAmount ?? 0, a?.Currency.Trim() ?? _options.NormalizedCurrency);
        }).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(Guid clinicId, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        return await ReconcileCoreAsync(db, clinicId, ct);
    }

    public async Task<QuoteResponse?> QuoteAsync(Guid clinicId, string eventType, string? countryCode, string? @operator, string? provider,
        DateTimeOffset? at, string? providerBilling = null, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var request = new RatingRequest(clinicId, (eventType ?? string.Empty).Trim().ToLowerInvariant(),
            RateResolver.NormalizeCountry(countryCode), RateResolver.NormalizeDimension(@operator), RateResolver.NormalizeDimension(provider),
            at ?? _time.GetUtcNow(),
            string.IsNullOrWhiteSpace(providerBilling) ? ProviderBillingResponsibility.PlatformFunded : providerBilling.Trim().ToLowerInvariant());
        var quote = await RateResolver.ResolveAsync(db, request, ct);
        if (quote is null) return null;
        var cardCode = await db.BillingRateCards.Where(c => c.Id == quote.RateCardId).Select(c => c.Code).FirstAsync(ct);
        return new QuoteResponse(request.EventType, request.CountryCode, request.Operator, request.Provider, quote.RateId, cardCode,
            quote.RateSource, quote.UnitPrice, quote.UnitProviderCost, quote.Currency, quote.Unit);
    }

    public async Task<BillingReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        // All usage that counted at the provider, whoever paid the provider and whether or not SculptFlow charged it.
        var usage = await db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ProviderOutcome == ProviderOutcome.Billable && u.OccurredAt >= from && u.OccurredAt < to)
            .GroupBy(u => new { u.ClinicId, u.Channel, u.EventType, u.ProviderBilling })
            .Select(g => new
            {
                g.Key.ClinicId, g.Key.Channel, g.Key.EventType, g.Key.ProviderBilling, Count = g.Count(), Quantity = g.Sum(u => u.Quantity),
                Revenue = g.Sum(u => u.ChargeStatus == ChargeStatus.Settled ? (u.Amount ?? 0) - u.RefundedAmount : 0),
                Cost = g.Sum(u => u.ProviderCost ?? 0)
            })
            .ToListAsync(ct);
        var clinicNames = await db.Clinics.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Ledger sign convention: charges are negative, so revenue is the negated sum.
        var subscriptionRevenue = -await db.BillingLedgerEntries.AsNoTracking()
            .Where(l => l.EntryType == LedgerEntryType.SubscriptionCharge && l.CreatedAt >= from && l.CreatedAt < to)
            .SumAsync(l => l.Amount, ct);
        var topUps = await db.BillingLedgerEntries.AsNoTracking()
            .Where(l => l.EntryType == LedgerEntryType.WalletTopUp && l.CreatedAt >= from && l.CreatedAt < to)
            .SumAsync(l => l.Amount, ct);

        var rows = usage
            .Select(x =>
            {
                var paidBySculptFlow = x.ProviderBilling == ProviderBillingResponsibility.PlatformFunded;
                return new BillingReportRow(x.ClinicId, clinicNames.TryGetValue(x.ClinicId, out var n) ? n : "?", x.Channel, x.EventType,
                    x.ProviderBilling, x.Count, x.Quantity, x.Revenue, x.Cost, x.Revenue - (paidBySculptFlow ? x.Cost : 0));
            })
            .OrderBy(r => r.ClinicName).ThenByDescending(r => r.Revenue).ToList();
        var revenue = rows.Sum(r => r.Revenue);
        var paidBySculptFlowCost = rows.Where(r => r.ProviderBilling == ProviderBillingResponsibility.PlatformFunded).Sum(r => r.ProviderCost);
        var paidExternallyCost = rows.Where(r => r.ProviderBilling != ProviderBillingResponsibility.PlatformFunded).Sum(r => r.ProviderCost);
        return new BillingReport(from, to, _options.NormalizedCurrency, rows, revenue, paidBySculptFlowCost, paidExternallyCost,
            revenue - paidBySculptFlowCost, subscriptionRevenue, topUps);
    }

    // ------------------------------------------------------------------------------------------------------------

    private async Task<ClinicBillingSummary> BuildSummaryAsync(Data.ApplicationDbContext db, Guid clinicId, CancellationToken ct)
    {
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.ClinicId == clinicId, ct);
        var subscription = await db.ClinicSubscriptions.AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p!.Entitlements)
            .FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        var now = _time.GetUtcNow();
        var plan = subscription?.Plan;

        DateTimeOffset? graceEnds = subscription?.Status == SubscriptionStatus.PastDue && subscription.PastDueSince is DateTimeOffset pastDueSince
            ? pastDueSince.AddDays(Math.Max(0, _options.GracePeriodDays))
            : null;
        var hasAccess = subscription is not null && (subscription.Status == SubscriptionStatus.Active
                                                     || (subscription.Status == SubscriptionStatus.PastDue && now < graceEnds));

        var values = plan?.Entitlements.ToDictionary(e => e.EntitlementKey, e => e.Value) ?? new Dictionary<string, string>();
        var entitlements = EntitlementCatalog.All.Select(d => new BillingEntitlementRow(d.Key, d.Label, d.Kind.ToString().ToLowerInvariant(),
            values.TryGetValue(d.Key, out var v) ? v : d.Kind == EntitlementKind.Feature ? "false" : "0")).ToList();

        // Usage of the current period (or the last 30 days without a subscription).
        var since = subscription?.CurrentPeriodStart ?? now.AddDays(-30);
        var usage = await db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.Settled && u.OccurredAt >= since)
            .GroupBy(u => u.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count(), Quantity = g.Sum(u => u.Quantity), Amount = g.Sum(u => (u.Amount ?? 0) - u.RefundedAmount) })
            .ToListAsync(ct);
        var usageRows = usage.OrderByDescending(u => u.Amount)
            .Select(u => new BillingUsageBreakdownRow(u.EventType, BillableEventTypes.Label(u.EventType), u.Count, u.Quantity, u.Amount))
            .ToList();

        // Usage SculptFlow didn't charge (paid to the provider directly, or no per-message fee) — shown apart.
        var uncharged = await db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.NotCharged
                        && u.ProviderOutcome != ProviderOutcome.NotBillable && u.OccurredAt >= since)
            .GroupBy(u => new { u.EventType, u.Channel, u.ProviderBilling })
            .Select(g => new { g.Key.EventType, g.Key.Channel, g.Key.ProviderBilling, Count = g.Count(), Quantity = g.Sum(u => u.Quantity) })
            .ToListAsync(ct);
        var providerDirect = uncharged.OrderByDescending(u => u.Count)
            .Select(u => new ProviderDirectUsageRow(u.EventType, BillableEventTypes.Label(u.EventType),
                ClinicPaidByLabel(u.ProviderBilling, u.Channel, charged: false), u.Count, u.Quantity))
            .ToList();

        var channelAccounts = (await _providerBilling.ListAccountsAsync(clinicId, ct))
            .Where(a => a.Status == ChannelIntegrationStatus.Connected)
            .Select(a => new ClinicChannelBillingRow(a.Channel, ChannelLabel(a.Channel), a.DisplayName,
                ClinicPaidByLabel(a.ProviderBilling, a.Channel, a.OmniUsageBilling), a.OmniUsageBilling))
            .ToList();

        var recent = await db.BillingLedgerEntries.AsNoTracking()
            .Where(l => l.ClinicId == clinicId)
            .OrderByDescending(l => l.Seq)
            .Take(RecentTransactions).ToListAsync(ct);

        var wallet = account?.WalletBalance ?? 0;
        var credit = account?.IncludedCreditBalance ?? 0;
        var reserved = account?.ReservedAmount ?? 0;
        return new ClinicBillingSummary(
            _options.Enabled,
            account?.Currency.Trim() ?? _options.NormalizedCurrency,
            plan?.Code, plan?.Name, plan?.Price, plan?.BillingPeriod,
            subscription?.Status, hasAccess,
            subscription?.CurrentPeriodStart, subscription?.CurrentPeriodEnd,
            subscription?.CancelAtPeriodEnd ?? false, graceEnds,
            plan?.IncludedUsageCredit ?? 0, credit, wallet, reserved, wallet + credit - reserved,
            entitlements, since, usageRows, usageRows.Sum(r => r.Amount),
            recent.Select(ToTransaction).ToList(),
            channelAccounts, providerDirect);
    }

    /// <summary>Who pays for usage, in clinic-facing words. Never names SculptFlow's own provider (white-label); the
    /// clinic's own Meta account may be named because it's theirs.</summary>
    public static string ClinicPaidByLabel(string providerBilling, string channel, bool charged)
    {
        var label = providerBilling switch
        {
            ProviderBillingResponsibility.PlatformFunded => charged ? "Billed by SculptFlow" : "Covered by SculptFlow",
            ProviderBillingResponsibility.CustomerDirect => channel == ChannelType.WhatsApp
                ? "Paid by you directly to Meta" : "Paid by you directly to the provider",
            ProviderBillingResponsibility.ExternalProviderDirect => "Paid by you to your messaging provider",
            ProviderBillingResponsibility.NoProviderUsageFee => "No per-message charge",
            _ => providerBilling
        };
        return charged && providerBilling != ProviderBillingResponsibility.PlatformFunded ? label + " · plus a SculptFlow usage fee" : label;
    }

    private static string ChannelLabel(string channel) => channel switch
    {
        ChannelType.WhatsApp => "WhatsApp",
        ChannelType.Telegram => "Telegram",
        ChannelType.Facebook => "Facebook",
        ChannelType.Instagram => "Instagram",
        _ => channel.Length == 0 ? channel : char.ToUpperInvariant(channel[0]) + channel[1..]
    };

    private static async Task<ReconciliationResponse> ReconcileCoreAsync(Data.ApplicationDbContext db, Guid clinicId, CancellationToken ct)
    {
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.ClinicId == clinicId, ct);
        var walletLedger = await db.BillingLedgerEntries.Where(l => l.ClinicId == clinicId && l.BalanceType == LedgerBalanceType.Wallet).SumAsync(l => l.Amount, ct);
        var creditLedger = await db.BillingLedgerEntries.Where(l => l.ClinicId == clinicId && l.BalanceType == LedgerBalanceType.IncludedCredit).SumAsync(l => l.Amount, ct);
        var openReservations = await db.BillingUsageRecords.Where(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.Reserved).SumAsync(u => u.ReservedAmount, ct);

        var wallet = account?.WalletBalance ?? 0;
        var credit = account?.IncludedCreditBalance ?? 0;
        var reserved = account?.ReservedAmount ?? 0;
        var balanced = wallet == walletLedger && credit == creditLedger && reserved == openReservations;
        return new ReconciliationResponse(balanced, wallet, walletLedger, credit, creditLedger, reserved, openReservations);
    }

    private static BillingTransactionRow ToTransaction(BillingLedgerEntry l) =>
        new(l.Id, l.CreatedAt, l.EntryType, LedgerLabel(l.EntryType), l.BalanceType, l.Amount, l.BalanceAfter, l.Reason);

    public static string LedgerLabel(string entryType) => entryType switch
    {
        LedgerEntryType.WalletTopUp => "Balance top-up",
        LedgerEntryType.UsageDebit => "Usage (wallet)",
        LedgerEntryType.IncludedCreditConsumption => "Usage (included credit)",
        LedgerEntryType.UsageRefund => "Usage refund",
        LedgerEntryType.SubscriptionCharge => "Subscription",
        LedgerEntryType.IncludedCreditGrant => "Included credit added",
        LedgerEntryType.IncludedCreditExpiry => "Included credit expired",
        LedgerEntryType.ManualAdjustment => "Adjustment",
        _ => entryType
    };

    internal static SubscriptionResponse ToSubscription(ClinicSubscription s) =>
        new(s.Id, s.ClinicId, s.Plan?.Code ?? "?", s.Plan?.Name ?? "?", s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd,
            s.CancelAtPeriodEnd, s.PastDueSince, s.EndedAt);

    private static (int Page, int PageSize) Clamp(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 200));
}
