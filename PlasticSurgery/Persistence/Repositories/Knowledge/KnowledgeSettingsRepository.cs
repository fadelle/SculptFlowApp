using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Persistence.Repositories.Knowledge;

public class KnowledgeSettingsRepository : IKnowledgeSettingsRepository
{
    private readonly ApplicationDbContext _db;

    public KnowledgeSettingsRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<KnowledgeSearchSettings?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.KnowledgeSearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public Task<KnowledgeSearchSettings?> GetAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.KnowledgeSearchSettings.FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public void Add(KnowledgeSearchSettings settings) => _db.KnowledgeSearchSettings.Add(settings);

    public void Detach(KnowledgeSearchSettings settings) => _db.Entry(settings).State = EntityState.Detached;
}
