using Microsoft.Extensions.Options;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.Billing;

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
