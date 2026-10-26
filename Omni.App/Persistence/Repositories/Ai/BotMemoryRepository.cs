using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Ai;

namespace Omni.App.Persistence.Repositories.Ai;

public class BotMemoryRepository : IBotMemoryRepository
{
    private readonly ApplicationDbContext _db;

    public BotMemoryRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<BotMemory>> ListRecentReadOnlyAsync(Guid key, bool test, int take, CancellationToken ct = default)
    {
        if (take <= 0) return [];
        var newest = await Of(key, test).AsNoTracking()
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(take).ToListAsync(ct);
        newest.Reverse();
        return newest;
    }

    public void Add(BotMemory memory) => _db.BotMemory.Add(memory);

    public async Task<int> DeleteAllButNewestAsync(Guid key, bool test, int keep, CancellationToken ct = default)
    {
        var kept = await Of(key, test).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(Math.Max(keep, 0)).Select(m => m.Id).ToListAsync(ct);
        return await Of(key, test).Where(m => !kept.Contains(m.Id)).ExecuteDeleteAsync(ct);
    }

    private IQueryable<BotMemory> Of(Guid key, bool test) =>
        test ? _db.BotMemory.Where(m => m.TestSessionId == key) : _db.BotMemory.Where(m => m.ConversationId == key);
}
