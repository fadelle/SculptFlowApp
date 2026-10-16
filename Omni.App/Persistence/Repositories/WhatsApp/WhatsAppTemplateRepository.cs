using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.WhatsApp;

namespace Omni.App.Persistence.Repositories.WhatsApp;

public class WhatsAppTemplateRepository : IWhatsAppTemplateRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public WhatsAppTemplateRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public void Add(WhatsAppTemplate template) => _db.WhatsAppTemplates.Add(template);

    public Task<WhatsAppTemplate?> GetAsync(Guid clientId, Guid templateId, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClientId == clientId && t.Id == templateId, ct);

    public async Task<IReadOnlyList<WhatsAppTemplate>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.WhatsAppTemplates(clientId), CacheArea.Channels,
            async token => await _db.WhatsAppTemplates.AsNoTracking()
                .Where(t => t.ClientId == clientId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(token), ct))!;

    public async Task<WhatsAppTemplate?> GetReadOnlyAsync(Guid clientId, Guid templateId, CancellationToken ct = default) =>
        (await ListForClientReadOnlyAsync(clientId, ct)).FirstOrDefault(t => t.Id == templateId);

    public Task<WhatsAppTemplate?> FindByProviderTemplateIdAsync(Guid clientId, string providerTemplateId, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClientId == clientId && t.MetaTemplateId == providerTemplateId, ct);

    public Task<WhatsAppTemplate?> FindByNameAsync(Guid clientId, string name, string language, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClientId == clientId && t.Name == name && t.Language == language, ct);
}
