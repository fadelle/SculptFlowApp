using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Per-clinic Knowledge Base settings (one persisted row per clinic). The chunking, indexing and search
/// services read their tuning and embedding parameters from here rather than from configuration; the
/// configuration (Embeddings:Model/Dimensions, Knowledge:*) is only the source of the DEFAULTS used
/// when a clinic's row is first created. Secrets (the embeddings API key) are never stored here.
/// </summary>
public interface IKnowledgeSettingsService
{
    /// <summary>Returns the clinic's settings, creating the row from the system defaults if it doesn't
    /// exist yet (safe under concurrent first use).</summary>
    Task<KnowledgeSettingsResponse> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Updates only chunk size, chunk overlap, top K and minimum similarity. Throws
    /// ArgumentException for out-of-range values. Read-only fields can't be reached through this method.
    /// Existing entries aren't re-chunked — new chunk settings apply to entries saved afterwards.</summary>
    Task<KnowledgeSettingsResponse> UpdateAsync(Guid clinicId, UpdateKnowledgeSettingsRequest request, CancellationToken ct = default);
}
