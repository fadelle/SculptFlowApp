using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.WhatsApp;

namespace PlasticSurgery.Persistence.Repositories.WhatsApp;

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

    public Task<WhatsAppTemplate?> GetAsync(Guid clinicId, Guid templateId, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Id == templateId, ct);

    public async Task<IReadOnlyList<WhatsAppTemplate>> ListForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.WhatsAppTemplates(clinicId), CacheArea.Channels,
            async token => await _db.WhatsAppTemplates.AsNoTracking()
                .Where(t => t.ClinicId == clinicId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(token), ct))!;

    public async Task<WhatsAppTemplate?> GetReadOnlyAsync(Guid clinicId, Guid templateId, CancellationToken ct = default) =>
        (await ListForClinicReadOnlyAsync(clinicId, ct)).FirstOrDefault(t => t.Id == templateId);

    public Task<WhatsAppTemplate?> FindByProviderTemplateIdAsync(Guid clinicId, string providerTemplateId, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.MetaTemplateId == providerTemplateId, ct);

    public Task<WhatsAppTemplate?> FindByNameAsync(Guid clinicId, string name, string language, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Name == name && t.Language == language, ct);
}
