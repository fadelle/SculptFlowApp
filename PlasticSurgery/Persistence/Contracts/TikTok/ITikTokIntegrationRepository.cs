using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.TikTok;

public interface ITikTokIntegrationRepository
{
    /// <summary>Tracked; one row per clinic.</summary>
    Task<TikTokIntegration?> GetAsync(Guid clinicId, CancellationToken ct = default);

    void Add(TikTokIntegration integration);
}
