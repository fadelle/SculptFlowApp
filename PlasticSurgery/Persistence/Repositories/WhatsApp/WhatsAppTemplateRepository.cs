using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.WhatsApp;

namespace PlasticSurgery.Persistence.Repositories.WhatsApp;

public class WhatsAppTemplateRepository : IWhatsAppTemplateRepository
{
    private readonly ApplicationDbContext _db;

    public WhatsAppTemplateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(WhatsAppTemplate template) => _db.WhatsAppTemplates.Add(template);

    public Task<WhatsAppTemplate?> GetAsync(Guid clinicId, Guid templateId, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Id == templateId, ct);

    public async Task<IReadOnlyList<WhatsAppTemplate>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.WhatsAppTemplates
            .Where(t => t.ClinicId == clinicId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task<WhatsAppTemplate?> FindByProviderTemplateIdAsync(Guid clinicId, string providerTemplateId, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.MetaTemplateId == providerTemplateId, ct);

    public Task<WhatsAppTemplate?> FindByNameAsync(Guid clinicId, string name, string language, CancellationToken ct = default) =>
        _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Name == name && t.Language == language, ct);

    public async Task<Guid?> FindInfobipTemplateClinicIdAsync(string? providerTemplateId, CancellationToken ct = default)
    {
        var owner = await _db.WhatsAppTemplates.AsNoTracking()
            .Where(t => t.MetaTemplateId == providerTemplateId && t.Provider == ChannelProvider.Infobip)
            .Select(t => new { t.ClinicId })
            .FirstOrDefaultAsync(ct);
        return owner?.ClinicId;
    }
}
