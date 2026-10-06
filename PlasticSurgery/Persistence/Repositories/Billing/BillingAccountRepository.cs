using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class BillingAccountRepository : IBillingAccountRepository
{
    private readonly ApplicationDbContext _db;

    public BillingAccountRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BillingAccount> LockAsync(Guid clinicId, string currency, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"insert into billing.accounts (id, clinic_id, currency) values ({Guid.NewGuid()}, {clinicId}, {currency}) on conflict (clinic_id) do nothing",
            ct);
        var rows = await _db.BillingAccounts
            .FromSqlInterpolated($"select * from billing.accounts where clinic_id = {clinicId} for update")
            .ToListAsync(ct);
        return rows.Single();
    }

    public Task<BillingAccount?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.ClinicId == clinicId, ct);

    public async Task<IReadOnlyDictionary<Guid, BillingAccount>> MapAllReadOnlyAsync(CancellationToken ct = default) =>
        await _db.BillingAccounts.AsNoTracking().ToDictionaryAsync(a => a.ClinicId, ct);

    public void Add(BillingAccount account) => _db.BillingAccounts.Add(account);
}
