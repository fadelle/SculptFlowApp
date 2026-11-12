using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Monitoring;

namespace Omni.App.Persistence.Repositories.Monitoring;

public class PlatformAdminWriteRepository : IPlatformAdminWriteRepository
{
    private const int PageSize = 50;
    private readonly ApplicationDbContext _db;

    public PlatformAdminWriteRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(PlatformAdminWrite write, CancellationToken ct = default)
    {
        _db.PlatformAdminWrites.Add(write);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PagedResponse<PlatformAdminWrite>> ListAsync(Guid? clientId, int page, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var query = _db.PlatformAdminWrites.AsNoTracking();
        if (clientId is { } id) query = query.Where(w => w.ClientId == id);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(w => w.OccurredAt).ThenByDescending(w => w.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new PagedResponse<PlatformAdminWrite>(items, total, page, PageSize);
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        _db.PlatformAdminWrites.Where(w => w.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
}
