using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.TikTok;

namespace PlasticSurgery.Persistence.Repositories.TikTok;

public class TikTokIntegrationRepository : ITikTokIntegrationRepository
{
    private readonly ApplicationDbContext _db;

    public TikTokIntegrationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<TikTokIntegration?> GetAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.TikTokIntegrations.FirstOrDefaultAsync(t => t.ClinicId == clinicId, ct);

    public void Add(TikTokIntegration integration) => _db.TikTokIntegrations.Add(integration);
}
