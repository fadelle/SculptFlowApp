using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Business.Contracts.Services.Knowledge;

/// <summary>
/// Per-client Knowledge Base settings (one persisted row per client). The chunking, indexing and search
/// services read their tuning and embedding parameters from here rather than from configuration; the
/// configuration (Embeddings:Model/Dimensions, Knowledge:*) is only the source of the DEFAULTS used
/// when a client's row is first created. Secrets (the embeddings API key) are never stored here.
/// </summary>
public interface IKnowledgeSettingsService
{
    /// <summary>Returns the client's settings, creating the row from the system defaults if it doesn't
    /// exist yet (safe under concurrent first use).</summary>
    Task<KnowledgeSettingsResponse> GetAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Updates only chunk size, chunk overlap, top K and minimum similarity. Throws
    /// ArgumentException for out-of-range values. Read-only fields can't be reached through this method.
    /// Existing entries aren't re-chunked — new chunk settings apply to entries saved afterwards.</summary>
    Task<KnowledgeSettingsResponse> UpdateAsync(Guid clientId, UpdateKnowledgeSettingsRequest request, CancellationToken ct = default);
}
