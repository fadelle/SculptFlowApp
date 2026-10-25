using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Ai;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.Ai;

public class BotRepository : IBotRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public BotRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Bot>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.Bots(clientId), CacheArea.Clients,
            async token => await _db.Bots.AsNoTracking().Include(b => b.KnowledgeSources)
                .Where(b => b.ClientId == clientId)
                .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id)
                .ToListAsync(token), ct))!;

    public Task<Bot?> GetAsync(Guid clientId, Guid botId, CancellationToken ct = default) =>
        _db.Bots.Include(b => b.KnowledgeSources).FirstOrDefaultAsync(b => b.ClientId == clientId && b.Id == botId, ct);

    public void Add(Bot bot) => _db.Bots.Add(bot);

    public void Remove(Bot bot) => _db.Bots.Remove(bot);

    public void RemoveSource(BotKnowledgeSource source) => _db.BotKnowledgeSources.Remove(source);
}
