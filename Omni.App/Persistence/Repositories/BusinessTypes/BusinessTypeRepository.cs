using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.BusinessTypes;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.BusinessTypes;

public class BusinessTypeRepository : IBusinessTypeRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public BusinessTypeRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<BusinessType>> ListReadOnlyAsync(CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.BusinessTypes, CacheArea.BusinessTypes, async token =>
        {
            var types = await _db.BusinessTypes.AsNoTracking().Include(t => t.Stages)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Code).ToListAsync(token);
            foreach (var type in types) SortStages(type);
            return types;
        }, ct))!;

    public async Task<BusinessType?> GetReadOnlyAsync(string code, CancellationToken ct = default) =>
        (await ListReadOnlyAsync(ct)).FirstOrDefault(t => t.Code == code);

    public async Task<BusinessType?> GetAsync(string code, CancellationToken ct = default)
    {
        var type = await _db.BusinessTypes.Include(t => t.Stages).FirstOrDefaultAsync(t => t.Code == code, ct);
        if (type is not null) SortStages(type);
        return type;
    }

    public Task<bool> ExistsAsync(string code, CancellationToken ct = default) =>
        _db.BusinessTypes.AnyAsync(t => t.Code == code, ct);

    public void Add(BusinessType type) => _db.BusinessTypes.Add(type);

    public void RemoveStage(PipelineStage stage) => _db.PipelineStages.Remove(stage);

    public async Task<IReadOnlyDictionary<string, int>> CountClientsByTypeAsync(CancellationToken ct = default) =>
        await _db.Clients.GroupBy(c => c.BusinessTypeCode)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<IReadOnlyDictionary<string, int>> CountLeadsByStageAsync(string code, CancellationToken ct = default) =>
        await (from l in _db.Leads
               join c in _db.Clients on l.ClientId equals c.Id
               where c.BusinessTypeCode == code
               group l by l.Status into g
               select new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static void SortStages(BusinessType type) =>
        type.Stages = type.Stages.OrderBy(s => s.SortOrder).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();
}
