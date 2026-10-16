using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Persistence.Repositories.Billing;

public class BillingAccountRepository : IBillingAccountRepository
{
    private readonly ApplicationDbContext _db;

    public BillingAccountRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BillingAccount> LockAsync(Guid clientId, string currency, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"insert into billing.accounts (id, client_id, currency) values ({Guid.NewGuid()}, {clientId}, {currency}) on conflict (client_id) do nothing",
            ct);
        var rows = await _db.BillingAccounts
            .FromSqlInterpolated($"select * from billing.accounts where client_id = {clientId} for update")
            .ToListAsync(ct);
        return rows.Single();
    }

    public Task<BillingAccount?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId, ct);

    public async Task<IReadOnlyDictionary<Guid, BillingAccount>> MapAllReadOnlyAsync(CancellationToken ct = default) =>
        await _db.BillingAccounts.AsNoTracking().ToDictionaryAsync(a => a.ClientId, ct);

    public void Add(BillingAccount account) => _db.BillingAccounts.Add(account);
}
