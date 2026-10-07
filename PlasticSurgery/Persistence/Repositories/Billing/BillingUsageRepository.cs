using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class BillingUsageRepository : IBillingUsageRepository
{
    private readonly ApplicationDbContext _db;

    public BillingUsageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(BillingUsageRecord usage) => _db.BillingUsageRecords.Add(usage);

    public Task<BillingUsageRecord?> FindByKeyAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default) =>
        _db.BillingUsageRecords.FirstOrDefaultAsync(u => u.ClinicId == clinicId && u.IdempotencyKey == idempotencyKey, ct);

    public Task<BillingUsageRecord?> FindByKeyReadOnlyAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default) =>
        _db.BillingUsageRecords.AsNoTracking().FirstOrDefaultAsync(u => u.ClinicId == clinicId && u.IdempotencyKey == idempotencyKey, ct);

    public Task<BillingUsageRecord?> GetAsync(Guid clinicId, Guid usageId, CancellationToken ct = default) =>
        _db.BillingUsageRecords.FirstOrDefaultAsync(u => u.ClinicId == clinicId && u.Id == usageId, ct);

    public async Task<IReadOnlyList<BillingUsageRecord>> ListStaleReadOnlyAsync(DateTimeOffset createdBefore, int take,
        CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.CreatedAt < createdBefore
                        && (u.ChargeStatus == ChargeStatus.Reserved
                            || (u.ChargeStatus == ChargeStatus.NotCharged && u.ProviderOutcome == ProviderOutcome.Pending)))
            .OrderBy(u => u.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<BillingUsageRecord> Items, int Total)> ListClinicUsageAsync(Guid clinicId, int skip, int take,
        CancellationToken ct = default)
    {
        var query = _db.BillingUsageRecords.AsNoTracking().Where(u => u.ClinicId == clinicId && u.ChargeStatus != ChargeStatus.Failed);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(u => u.OccurredAt).Skip(skip).Take(take).ToListAsync(ct);
        return (rows, total);
    }

    public async Task<(IReadOnlyList<BillingUsageRecord> Items, int Total)> ListAdminUsageAsync(Guid clinicId, string? chargeStatus,
        string? eventType, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.BillingUsageRecords.AsNoTracking().Where(u => u.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(chargeStatus)) query = query.Where(u => u.ChargeStatus == chargeStatus);
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(u => u.EventType == eventType);
        if (from is not null) query = query.Where(u => u.OccurredAt >= from);
        if (to is not null) query = query.Where(u => u.OccurredAt < to);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(u => u.OccurredAt).Skip(skip).Take(take).ToListAsync(ct);
        return (rows, total);
    }

    public Task<int> CountReservedAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.BillingUsageRecords.CountAsync(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.Reserved, ct);

    public Task<decimal> SumReservedAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.BillingUsageRecords.Where(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.Reserved).SumAsync(u => u.ReservedAmount, ct);

    public async Task<IReadOnlyList<SettledUsageGroup>> GroupSettledAsync(Guid clinicId, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.Settled && u.OccurredAt >= since)
            .GroupBy(u => u.EventType)
            .Select(g => new SettledUsageGroup(g.Key, g.Count(), g.Sum(u => u.Quantity), g.Sum(u => (u.Amount ?? 0) - u.RefundedAmount)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UnchargedUsageGroup>> GroupUnchargedAsync(Guid clinicId, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.ChargeStatus == ChargeStatus.NotCharged
                        && u.ProviderOutcome != ProviderOutcome.NotBillable && u.OccurredAt >= since)
            .GroupBy(u => new { u.EventType, u.Channel, u.ProviderBilling })
            .Select(g => new UnchargedUsageGroup(g.Key.EventType, g.Key.Channel, g.Key.ProviderBilling, g.Count(), g.Sum(u => u.Quantity)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BillableUsageGroup>> GroupBillableAsync(Guid clinicId, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClinicId == clinicId && u.ProviderOutcome == ProviderOutcome.Billable && u.OccurredAt >= since)
            .GroupBy(u => new { u.EventType, u.Channel, u.ProviderBilling })
            .Select(g => new BillableUsageGroup(
                g.Key.EventType, g.Key.Channel, g.Key.ProviderBilling, g.Count(), g.Sum(u => u.Quantity),
                g.Sum(u => u.ChargeStatus == ChargeStatus.Settled ? (u.Amount ?? 0) - u.RefundedAmount : 0),
                g.Sum(u => u.ProviderCost ?? 0)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReportUsageGroup>> GroupBillableForReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ProviderOutcome == ProviderOutcome.Billable && u.OccurredAt >= from && u.OccurredAt < to)
            .GroupBy(u => new { u.ClinicId, u.Channel, u.EventType, u.ProviderBilling })
            .Select(g => new ReportUsageGroup(
                g.Key.ClinicId, g.Key.Channel, g.Key.EventType, g.Key.ProviderBilling, g.Count(), g.Sum(u => u.Quantity),
                g.Sum(u => u.ChargeStatus == ChargeStatus.Settled ? (u.Amount ?? 0) - u.RefundedAmount : 0),
                g.Sum(u => u.ProviderCost ?? 0)))
            .ToListAsync(ct);
}
