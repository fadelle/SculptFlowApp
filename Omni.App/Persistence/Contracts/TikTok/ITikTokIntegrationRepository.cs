using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.TikTok;

public interface ITikTokIntegrationRepository
{
    /// <summary>Tracked; one row per client.</summary>
    Task<TikTokIntegration?> GetAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Read-only copy (cached).</summary>
    Task<TikTokIntegration?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default);
    void Add(TikTokIntegration integration);
}
