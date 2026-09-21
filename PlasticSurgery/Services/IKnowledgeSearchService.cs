using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Semantic search over ONE clinic's Knowledge Base. Returns relevant chunks only — it never
/// composes an answer (that's the AI agent's job) and never returns another clinic's knowledge.
/// </summary>
public interface IKnowledgeSearchService
{
    /// <summary>Embeds the query, then ranks only chunks with clinic_id = clinicId whose parent document
    /// is active, by cosine similarity. Uses the clinic's persisted settings: at most Top K results (an
    /// explicit limit can only lower that), and chunks scoring below the clinic's minimum similarity are
    /// dropped, so an unrelated query yields an empty list rather than the least-bad match. Throws
    /// InvalidOperationException if the embedding call fails.</summary>
    Task<KnowledgeSearchResponse> SearchAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default);

    /// <summary>The same search, but returns every top-K candidate in rank order WITH its chunk id and whether
    /// it cleared the clinic's minimum similarity. <see cref="SearchAsync"/> is exactly this list filtered to
    /// the candidates that cleared it — there is one retrieval implementation, and this is its raw output.
    /// Used by the Retrieval Benchmark, which needs chunk ids to check the exact expected chunk.</summary>
    Task<IReadOnlyList<RankedKnowledgeChunk>> SearchRankedAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default);
}

/// <summary>One top-K candidate from the similarity search. <see cref="Score"/> is the raw cosine similarity;
/// <see cref="MeetsMinimumSimilarity"/> says whether production search would actually return it.</summary>
public record RankedKnowledgeChunk(
    Guid ChunkId,
    Guid DocumentId,
    string Title,
    string Category,
    string Content,
    double Score,
    bool MeetsMinimumSimilarity);
