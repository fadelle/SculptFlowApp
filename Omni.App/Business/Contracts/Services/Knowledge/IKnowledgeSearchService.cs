using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Business.Contracts.Services.Knowledge;

/// <summary>
/// Semantic search over ONE client's Knowledge Base. Returns relevant chunks only — it never
/// composes an answer (that's the AI assistant's job) and never returns another client's knowledge.
/// </summary>
public interface IKnowledgeSearchService
{
    /// <summary>Embeds the query, then ranks only chunks with client_id = clientId whose parent document
    /// is active, by cosine similarity. Uses the client's persisted settings: at most Top K results (an
    /// explicit limit can only lower that), and chunks scoring below the client's minimum similarity are
    /// dropped, so an unrelated query yields an empty list rather than the least-bad match. Throws
    /// InvalidOperationException if the embedding call fails.</summary>
    Task<KnowledgeSearchResponse> SearchAsync(Guid clientId, string query, int? limit = null, CancellationToken ct = default);

    /// <summary>The same search, but returns every top-K candidate in rank order WITH its chunk id and whether
    /// it cleared the client's minimum similarity. <see cref="SearchAsync"/> is exactly this list filtered to
    /// the candidates that cleared it — there is one retrieval implementation, and this is its raw output.
    /// Used by the Retrieval Benchmark, which needs chunk ids to check the exact expected chunk.</summary>
    Task<IReadOnlyList<RankedKnowledgeChunk>> SearchRankedAsync(Guid clientId, string query, int? limit = null, CancellationToken ct = default);
}
