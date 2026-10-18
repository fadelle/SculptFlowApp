using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Engines.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Business.Services.Billing;

public class BillingQueryService : IBillingQueryService
{
    private readonly IBillingUnitOfWorkFactory _units;
    private readonly TimeProvider _time;
    private readonly IProviderBillingService _providerBilling;
    private readonly IConfigManager _config;

    public BillingQueryService(IBillingUnitOfWorkFactory units, TimeProvider time, IProviderBillingService providerBilling, IConfigManager config)
    {
        _config = config;
        _units = units;
        _time = time;
        _providerBilling = providerBilling;
    }

    public async Task<ClientBillingSummary> GetSummaryAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        return await BuildSummaryAsync(unit, clientId, ct);
    }

    public async Task<PagedResponse<ClientUsageRow>> ListClientUsageAsync(Guid clientId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var unit = _units.Create();
        // Failed attempts (refused for balance/price) are kept for support but aren't usage the client paid for.
        var (rows, total) = await unit.Usage.ListClientUsageAsync(clientId, (page - 1) * pageSize, pageSize, ct);
        return new PagedResponse<ClientUsageRow>(rows.Select(u =>
        {
            var charged = u.ChargeStatus != ChargeStatus.NotCharged;
            return new ClientUsageRow(
                u.Id, u.OccurredAt, u.EventType, BillableEventTypes.Label(u.EventType), u.Channel, u.Quantity, u.Unit, u.CountryCode,
                u.UnitPrice, u.Amount, u.CreditAmount, u.WalletAmount, u.RefundedAmount, u.ChargeStatus, u.FailureReason,
                charged, ClientPaidByLabel(u.ProviderBilling, u.Channel, charged));
        }).ToList(), total, page, pageSize);
    }

    public async Task<PagedResponse<BillingTransactionRow>> ListTransactionsAsync(Guid clientId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var unit = _units.Create();
        var (rows, total) = await unit.Ledger.ListAsync(clientId, (page - 1) * pageSize, pageSize, ct);
        return new PagedResponse<BillingTransactionRow>(rows.Select(ToTransaction).ToList(), total, page, pageSize);
    }

    public async Task<AdminBillingOverview?> GetAdminOverviewAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var client = await unit.Clients.GetReadOnlyAsync(clientId, ct);
        if (client is null) return null;

        var summary = await BuildSummaryAsync(unit, clientId, ct);
        var subscription = await unit.Subscriptions.GetWithPlanReadOnlyAsync(clientId, ct);
        var customCard = await unit.RateCards.GetClientCardCodeAsync(clientId, ct);

        // Everything that counted at the provider this period, charged by Omni or not.
        var usage = await unit.Usage.GroupBillableAsync(clientId, summary.UsageSince, ct);
        var openReservations = await unit.Usage.CountReservedAsync(clientId, ct);

        return new AdminBillingOverview(
            clientId, client.Name, summary,
            subscription is null ? null : ToSubscription(subscription),
            customCard,
            usage.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Count)
                .Select(x => new AdminUsageBreakdownRow(x.EventType, x.Channel, x.ProviderBilling, x.Count, x.Quantity, x.Amount, x.Cost,
                    x.Amount - (x.ProviderBilling == ProviderBillingResponsibility.PlatformFunded ? x.Cost : 0))).ToList(),
            openReservations,
            await ReconcileCoreAsync(unit, clientId, ct),
            await _providerBilling.ListAccountsAsync(clientId, ct));
    }

    public async Task<PagedResponse<AdminUsageRow>> ListAdminUsageAsync(Guid clientId, string? status, string? eventType, DateTimeOffset? from,
        DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var unit = _units.Create();
        var (rows, total) = await unit.Usage.ListAdminUsageAsync(clientId, status, eventType, from, to, (page - 1) * pageSize, pageSize, ct);
        return new PagedResponse<AdminUsageRow>(rows.Select(u => new AdminUsageRow(
            u.Id, u.IdempotencyKey, u.OccurredAt, u.EventType, u.Channel, u.ChannelIntegrationId, u.ProviderBilling, u.Quantity, u.Unit,
            u.CountryCode, u.Operator, u.Provider,
            u.RateSource, u.RateId, u.UnitProviderCost, u.UnitPrice, u.ProviderCost, u.Amount, u.ReservedAmount, u.CreditAmount,
            u.WalletAmount, u.RefundedAmount, u.Currency.Trim(), u.ChargeStatus, u.ProviderOutcome, u.FailureReason, u.ReleaseReason, u.MessageId,
            u.ConversationId, u.CampaignId, u.Source, u.Actor, u.ReservedAt, u.SettledAt, u.ReleasedAt, u.RefundedAt)).ToList(),
            total, page, pageSize);
    }

    public async Task<PagedResponse<AdminLedgerRow>> ListAdminLedgerAsync(Guid clientId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);
        await using var unit = _units.Create();
        var (rows, total) = await unit.Ledger.ListAsync(clientId, (page - 1) * pageSize, pageSize, ct);
        return new PagedResponse<AdminLedgerRow>(rows.Select(l => new AdminLedgerRow(
            l.Id, l.CreatedAt, l.EntryType, l.BalanceType, l.Amount, l.BalanceAfter, l.Currency.Trim(), l.IdempotencyKey,
            l.UsageRecordId, l.SubscriptionId, l.PlanId, l.Source, l.Actor, l.Reason, l.Reference, l.CorrelationId)).ToList(),
            total, page, pageSize);
    }

    public async Task<IReadOnlyList<BillingAccountRow>> ListAccountsAsync(CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var clients = await unit.Clients.ListNamesAsync(ct);
        var accounts = await unit.Accounts.MapAllReadOnlyAsync(ct);
        var subscriptions = await unit.Subscriptions.MapAllWithPlanReadOnlyAsync(ct);
        return clients.Select(c =>
        {
            accounts.TryGetValue(c.Id, out var a);
            subscriptions.TryGetValue(c.Id, out var s);
            return new BillingAccountRow(c.Id, c.Name, s?.Plan?.Code, s?.Status, s?.CurrentPeriodEnd,
                a?.WalletBalance ?? 0, a?.IncludedCreditBalance ?? 0, a?.ReservedAmount ?? 0, a?.Currency.Trim() ?? _config.BillingCurrency);
        }).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        return await ReconcileCoreAsync(unit, clientId, ct);
    }

    public async Task<QuoteResponse?> QuoteAsync(Guid clientId, string eventType, string? countryCode, string? @operator, string? provider,
        DateTimeOffset? at, string? providerBilling = null, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var request = new RatingRequest(clientId, (eventType ?? string.Empty).Trim().ToLowerInvariant(),
            RateResolver.NormalizeCountry(countryCode), RateResolver.NormalizeDimension(@operator), RateResolver.NormalizeDimension(provider),
            at ?? _time.GetUtcNow(),
            string.IsNullOrWhiteSpace(providerBilling) ? ProviderBillingResponsibility.PlatformFunded : providerBilling.Trim().ToLowerInvariant());
        var quote = await RateResolver.ResolveAsync(unit.RateCards, request, ct);
        if (quote is null) return null;
        var cardCode = await unit.RateCards.GetCodeAsync(quote.RateCardId, ct);
        return new QuoteResponse(request.EventType, request.CountryCode, request.Operator, request.Provider, quote.RateId, cardCode,
            quote.RateSource, quote.UnitPrice, quote.UnitProviderCost, quote.Currency, quote.Unit);
    }

    public async Task<BillingReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        // All usage that counted at the provider, whoever paid the provider and whether or not Omni charged it.
        var usage = await unit.Usage.GroupBillableForReportAsync(from, to, ct);
        var clientNames = (await unit.Clients.ListNamesAsync(ct)).ToDictionary(c => c.Id, c => c.Name);

        // Ledger sign convention: charges are negative, so revenue is the negated sum.
        var subscriptionRevenue = -await unit.Ledger.SumOfTypeAsync(LedgerEntryType.SubscriptionCharge, from, to, ct);
        var topUps = await unit.Ledger.SumOfTypeAsync(LedgerEntryType.WalletTopUp, from, to, ct);

        var rows = usage
            .Select(x =>
            {
                var paidByPlatform = x.ProviderBilling == ProviderBillingResponsibility.PlatformFunded;
                return new BillingReportRow(x.ClientId, clientNames.TryGetValue(x.ClientId, out var n) ? n : "?", x.Channel, x.EventType,
                    x.ProviderBilling, x.Count, x.Quantity, x.Revenue, x.Cost, x.Revenue - (paidByPlatform ? x.Cost : 0));
            })
            .OrderBy(r => r.ClientName).ThenByDescending(r => r.Revenue).ToList();
        var revenue = rows.Sum(r => r.Revenue);
        var paidByPlatformCost = rows.Where(r => r.ProviderBilling == ProviderBillingResponsibility.PlatformFunded).Sum(r => r.ProviderCost);
        var paidExternallyCost = rows.Where(r => r.ProviderBilling != ProviderBillingResponsibility.PlatformFunded).Sum(r => r.ProviderCost);
        return new BillingReport(from, to, _config.BillingCurrency, rows, revenue, paidByPlatformCost, paidExternallyCost,
            revenue - paidByPlatformCost, subscriptionRevenue, topUps);
    }

    // ------------------------------------------------------------------------------------------------------------

    private async Task<ClientBillingSummary> BuildSummaryAsync(IBillingUnitOfWork unit, Guid clientId, CancellationToken ct)
    {
        var account = await unit.Accounts.GetReadOnlyAsync(clientId, ct);
        var subscription = await unit.Subscriptions.GetWithEntitlementsReadOnlyAsync(clientId, ct);
        var now = _time.GetUtcNow();
        var plan = subscription?.Plan;

        DateTimeOffset? graceEnds = subscription?.Status == SubscriptionStatus.PastDue && subscription.PastDueSince is DateTimeOffset pastDueSince
            ? pastDueSince.AddDays(_config.BillingGracePeriodDays)
            : null;
        var hasAccess = subscription is not null && (subscription.Status == SubscriptionStatus.Active
                                                     || (subscription.Status == SubscriptionStatus.PastDue && now < graceEnds));

        var values = plan?.Entitlements.ToDictionary(e => e.EntitlementKey, e => e.Value) ?? new Dictionary<string, string>();
        var entitlements = EntitlementCatalog.All.Select(d => new BillingEntitlementRow(d.Key, d.Label, d.Kind.ToString().ToLowerInvariant(),
            values.TryGetValue(d.Key, out var v) ? v : d.Kind == EntitlementKind.Feature ? "false" : "0")).ToList();

        // Usage of the current period (or the last 30 days without a subscription).
        var since = subscription?.CurrentPeriodStart ?? now.AddDays(-30);
        var usage = await unit.Usage.GroupSettledAsync(clientId, since, ct);
        var usageRows = usage.OrderByDescending(u => u.Amount)
            .Select(u => new BillingUsageBreakdownRow(u.EventType, BillableEventTypes.Label(u.EventType), u.Count, u.Quantity, u.Amount))
            .ToList();

        // Usage Omni didn't charge (paid to the provider directly, or no per-message fee) — shown apart.
        var uncharged = await unit.Usage.GroupUnchargedAsync(clientId, since, ct);
        var providerDirect = uncharged.OrderByDescending(u => u.Count)
            .Select(u => new ProviderDirectUsageRow(u.EventType, BillableEventTypes.Label(u.EventType),
                ClientPaidByLabel(u.ProviderBilling, u.Channel, charged: false), u.Count, u.Quantity, u.ProviderBilling, u.Channel))
            .ToList();

        var channelAccounts = (await _providerBilling.ListAccountsAsync(clientId, ct))
            .Where(a => a.Status == ChannelIntegrationStatus.Connected)
            .Select(a => new ClientChannelBillingRow(a.Channel, ChannelLabel(a.Channel), a.DisplayName,
                ClientPaidByLabel(a.ProviderBilling, a.Channel, a.OmniUsageBilling), a.OmniUsageBilling, a.ProviderBilling))
            .ToList();

        var (recent, _) = await unit.Ledger.ListAsync(clientId, 0, _config.BillingRecentTransactions, ct);

        var wallet = account?.WalletBalance ?? 0;
        var credit = account?.IncludedCreditBalance ?? 0;
        var reserved = account?.ReservedAmount ?? 0;
        return new ClientBillingSummary(
            _config.BillingEnabled,
            account?.Currency.Trim() ?? _config.BillingCurrency,
            plan?.Code, plan?.Name, plan?.Price, plan?.BillingPeriod,
            subscription?.Status, hasAccess,
            subscription?.CurrentPeriodStart, subscription?.CurrentPeriodEnd,
            subscription?.CancelAtPeriodEnd ?? false, graceEnds,
            plan?.IncludedUsageCredit ?? 0, credit, wallet, reserved, wallet + credit - reserved,
            entitlements, since, usageRows, usageRows.Sum(r => r.Amount),
            recent.Select(ToTransaction).ToList(),
            channelAccounts, providerDirect);
    }

    /// <summary>Who pays for usage, in client-facing English words (the admin portal; the client's Billing page uses
    /// BillingTextHelper.PaidByLabel). Never names Omni's own provider (white-label); the client's own Meta account may be
    /// named because it's theirs.</summary>
    public static string ClientPaidByLabel(string providerBilling, string channel, bool charged)
    {
        var label = providerBilling switch
        {
            ProviderBillingResponsibility.PlatformFunded => charged ? "Billed by Omni" : "Covered by Omni",
            ProviderBillingResponsibility.CustomerDirect => channel == ChannelType.WhatsApp
                ? "Paid by you directly to Meta" : "Paid by you directly to the provider",
            ProviderBillingResponsibility.ExternalProviderDirect => "Paid by you to your messaging provider",
            ProviderBillingResponsibility.NoProviderUsageFee => "No per-message charge",
            _ => providerBilling
        };
        return charged && providerBilling != ProviderBillingResponsibility.PlatformFunded ? label + " · plus an Omni usage fee" : label;
    }

    private static string ChannelLabel(string channel) => channel switch
    {
        ChannelType.WhatsApp => "WhatsApp",
        ChannelType.Telegram => "Telegram",
        ChannelType.Facebook => "Facebook",
        ChannelType.Instagram => "Instagram",
        _ => channel.Length == 0 ? channel : char.ToUpperInvariant(channel[0]) + channel[1..]
    };

    private static async Task<ReconciliationResponse> ReconcileCoreAsync(IBillingUnitOfWork unit, Guid clientId, CancellationToken ct)
    {
        var account = await unit.Accounts.GetReadOnlyAsync(clientId, ct);
        var walletLedger = await unit.Ledger.SumAsync(clientId, LedgerBalanceType.Wallet, ct);
        var creditLedger = await unit.Ledger.SumAsync(clientId, LedgerBalanceType.IncludedCredit, ct);
        var openReservations = await unit.Usage.SumReservedAsync(clientId, ct);

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

    internal static SubscriptionResponse ToSubscription(ClientSubscription s) =>
        new(s.Id, s.ClientId, s.Plan?.Code ?? "?", s.Plan?.Name ?? "?", s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd,
            s.CancelAtPeriodEnd, s.PastDueSince, s.EndedAt);

    private static (int Page, int PageSize) Clamp(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 200));
}
