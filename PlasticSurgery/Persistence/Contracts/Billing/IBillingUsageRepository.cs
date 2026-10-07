using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>billing.usage_records. Tracked unless "ReadOnly".</summary>
public interface IBillingUsageRepository
{
    void Add(BillingUsageRecord usage);

    Task<BillingUsageRecord?> FindByKeyAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default);

    Task<BillingUsageRecord?> FindByKeyReadOnlyAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default);

    Task<BillingUsageRecord?> GetAsync(Guid clinicId, Guid usageId, CancellationToken ct = default);

    /// <summary>Open holds, and uncharged usage whose provider outcome never arrived, created before the cutoff; oldest first.</summary>
    Task<IReadOnlyList<BillingUsageRecord>> ListStaleReadOnlyAsync(DateTimeOffset createdBefore, int take, CancellationToken ct = default);

    /// <summary>Everything but failed attempts, newest first.</summary>
    Task<(IReadOnlyList<BillingUsageRecord> Items, int Total)> ListClinicUsageAsync(Guid clinicId, int skip, int take,
        CancellationToken ct = default);

    /// <summary>Every record (failed included), filtered, newest first.</summary>
    Task<(IReadOnlyList<BillingUsageRecord> Items, int Total)> ListAdminUsageAsync(Guid clinicId, string? chargeStatus, string? eventType,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default);

    Task<int> CountReservedAsync(Guid clinicId, CancellationToken ct = default);

    Task<decimal> SumReservedAsync(Guid clinicId, CancellationToken ct = default);

    Task<IReadOnlyList<SettledUsageGroup>> GroupSettledAsync(Guid clinicId, DateTimeOffset since, CancellationToken ct = default);

    Task<IReadOnlyList<UnchargedUsageGroup>> GroupUnchargedAsync(Guid clinicId, DateTimeOffset since, CancellationToken ct = default);

    Task<IReadOnlyList<BillableUsageGroup>> GroupBillableAsync(Guid clinicId, DateTimeOffset since, CancellationToken ct = default);

    Task<IReadOnlyList<ReportUsageGroup>> GroupBillableForReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
