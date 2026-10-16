using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.TikTok;

public interface ITikTokIntegrationRepository
{
    /// <summary>Tracked; one row per clinic.</summary>
    Task<TikTokIntegration?> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only copy (cached).</summary>
    Task<TikTokIntegration?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default);
    void Add(TikTokIntegration integration);
}
