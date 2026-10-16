using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Knowledge;

namespace Omni.App.Persistence.Repositories.Knowledge;

public class KnowledgeSettingsRepository : IKnowledgeSettingsRepository
{
    private readonly ApplicationDbContext _db;

    public KnowledgeSettingsRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<KnowledgeSearchSettings?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _db.KnowledgeSearchSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public Task<KnowledgeSearchSettings?> GetAsync(Guid clientId, CancellationToken ct = default) =>
        _db.KnowledgeSearchSettings.FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public void Add(KnowledgeSearchSettings settings) => _db.KnowledgeSearchSettings.Add(settings);

    public void Detach(KnowledgeSearchSettings settings) => _db.Entry(settings).State = EntityState.Detached;
}
