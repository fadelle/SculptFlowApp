using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class BillingLedgerRepository : IBillingLedgerRepository
{
    private readonly ApplicationDbContext _db;

    public BillingLedgerRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(BillingLedgerEntry entry) => _db.BillingLedgerEntries.Add(entry);

    public Task<BillingLedgerEntry?> FindByKeyReadOnlyAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default) =>
        _db.BillingLedgerEntries.AsNoTracking().FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> KeyExistsAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default) =>
        _db.BillingLedgerEntries.AnyAsync(e => e.ClinicId == clinicId && e.IdempotencyKey == idempotencyKey, ct);

    public async Task<(IReadOnlyList<BillingLedgerEntry> Items, int Total)> ListAsync(Guid clinicId, int skip, int take,
        CancellationToken ct = default)
    {
        var query = _db.BillingLedgerEntries.AsNoTracking().Where(l => l.ClinicId == clinicId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(l => l.Seq).Skip(skip).Take(take).ToListAsync(ct);
        return (rows, total);
    }

    public Task<decimal> SumAsync(Guid clinicId, string balanceType, CancellationToken ct = default) =>
        _db.BillingLedgerEntries.Where(l => l.ClinicId == clinicId && l.BalanceType == balanceType).SumAsync(l => l.Amount, ct);

    public Task<decimal> SumOfTypeAsync(string entryType, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        _db.BillingLedgerEntries.AsNoTracking()
            .Where(l => l.EntryType == entryType && l.CreatedAt >= from && l.CreatedAt < to)
            .SumAsync(l => l.Amount, ct);
}
