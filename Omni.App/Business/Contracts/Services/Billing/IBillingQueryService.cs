using Microsoft.Extensions.Options;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>Read side of billing: the client's billing view, usage and transaction history, admin overviews,
/// reconciliation (cached balances vs the ledger), price quotes and a revenue/cost/margin report. Nothing here
/// changes data.</summary>
public interface IBillingQueryService
{
    /// <summary>Client-safe summary (no provider names, costs or margins).</summary>
    Task<ClientBillingSummary> GetSummaryAsync(Guid clientId, CancellationToken ct = default);
    Task<PagedResponse<ClientUsageRow>> ListClientUsageAsync(Guid clientId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResponse<BillingTransactionRow>> ListTransactionsAsync(Guid clientId, int page, int pageSize, CancellationToken ct = default);

    Task<AdminBillingOverview?> GetAdminOverviewAsync(Guid clientId, CancellationToken ct = default);
    Task<PagedResponse<AdminUsageRow>> ListAdminUsageAsync(Guid clientId, string? status, string? eventType, DateTimeOffset? from,
        DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResponse<AdminLedgerRow>> ListAdminLedgerAsync(Guid clientId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<BillingAccountRow>> ListAccountsAsync(CancellationToken ct = default);
    Task<ReconciliationResponse> ReconcileAsync(Guid clientId, CancellationToken ct = default);
    /// <summary>What Omni would charge for the event on an account with this provider billing (default:
    /// Omni-funded). Null when nothing prices it.</summary>
    Task<QuoteResponse?> QuoteAsync(Guid clientId, string eventType, string? countryCode, string? @operator, string? provider,
        DateTimeOffset? at, string? providerBilling = null, CancellationToken ct = default);
    Task<BillingReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
