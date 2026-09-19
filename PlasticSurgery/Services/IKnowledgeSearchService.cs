using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Semantic search over ONE clinic's Knowledge Base. Returns relevant chunks only — it never
/// composes an answer (that's the AI agent's job) and never returns another clinic's knowledge.
/// </summary>
public interface IKnowledgeSearchService
{
    /// <summary>Embeds the query, then ranks only chunks with clinic_id = clinicId whose parent document
    /// is active, by cosine similarity. limit defaults to 5 and is capped at 10. Chunks scoring below
    /// Knowledge:MinScore (default 0.30) are dropped, so an unrelated query yields an empty list rather
    /// than the least-bad match. Throws InvalidOperationException if the embedding call fails.</summary>
    Task<KnowledgeSearchResponse> SearchAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default);
}
